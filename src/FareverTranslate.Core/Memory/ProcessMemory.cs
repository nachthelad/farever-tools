using System.ComponentModel;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace FareverTranslate.Core.Memory;

public sealed record ProcessMemoryScanSummary(
    int RegionCount,
    long TotalBytes,
    long LargestRegionBytes,
    int SmallRegionCount,
    long SmallRegionBytes,
    int MediumRegionCount,
    long MediumRegionBytes,
    int LargeRegionCount,
    long LargeRegionBytes);

public sealed class ProcessMemory : IProcessMemory
{
    private const int ChunkSize = 4 * 1024 * 1024;
    private const long MaximumUserAddress = 0x00007FFFFFFEFFFF;
    private readonly NativeMethods.SafeProcessMemoryHandle handle;
    private readonly long mainModuleBase;
    private readonly int mainModuleSize;
    private long hashLinkTypes;
    private int hashLinkTypeCount;
    private bool hashLinkTypeTableLocated;

    public ProcessMemory(int processId)
    {
        handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation | NativeMethods.ProcessVmRead,
            inheritHandle: false,
            processId);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "No se pudo abrir Farever con acceso de solo lectura.");
        }

        using Process process = Process.GetProcessById(processId);
        ProcessModule? mainModule = process.MainModule;
        if (mainModule is null)
        {
            throw new InvalidOperationException(
                "No se pudo localizar el ejecutable de Farever.");
        }
        mainModuleBase = mainModule.BaseAddress.ToInt64();
        mainModuleSize = mainModule.ModuleMemorySize;
    }

    public bool TryReadBytes(long address, int count, out byte[] buffer)
    {
        buffer = Array.Empty<byte>();
        if (!IsUserAddress(address) || count <= 0 || count > 16 * 1024 * 1024)
        {
            return false;
        }

        var candidate = new byte[count];
        if (!NativeMethods.ReadProcessMemory(
                handle,
                new IntPtr(address),
                candidate,
                new IntPtr(count),
                out IntPtr read)
            || read.ToInt64() != count)
        {
            return false;
        }

        buffer = candidate;
        return true;
    }

    public bool TryReadPointer(long address, out long value)
    {
        value = 0;
        if (!TryReadBytes(address, sizeof(long), out byte[] bytes))
        {
            return false;
        }
        value = BitConverter.ToInt64(bytes);
        return true;
    }

    public bool TryReadInt32(long address, out int value)
    {
        value = 0;
        if (!TryReadBytes(address, sizeof(int), out byte[] bytes))
        {
            return false;
        }
        value = BitConverter.ToInt32(bytes);
        return true;
    }

    public bool TryReadByte(long address, out byte value)
    {
        value = 0;
        if (!TryReadBytes(address, sizeof(byte), out byte[] bytes))
        {
            return false;
        }
        value = bytes[0];
        return true;
    }

    public string? ReadNativeString(long address, int maximumBytes = 512)
    {
        if (!IsUserAddress(address) || maximumBytes <= 0)
        {
            return null;
        }

        var collected = new List<byte>(Math.Min(maximumBytes, 512));
        int remaining = maximumBytes;
        long current = address;
        while (remaining > 0)
        {
            int bytesToPageBoundary =
                4096 - (int)(current & (4096 - 1));
            int requested = Math.Min(remaining, bytesToPageBoundary);
            if (!TryReadBytes(current, requested, out byte[] page))
            {
                return collected.Count == 0
                    ? null
                    : DecodeNativeString(collected.ToArray());
            }
            collected.AddRange(page);
            byte[] candidate = collected.ToArray();
            if (ContainsNativeTerminator(candidate))
            {
                return DecodeNativeString(candidate);
            }
            current += requested;
            remaining -= requested;
        }
        return DecodeNativeString(collected.ToArray());
    }

    private static bool ContainsNativeTerminator(byte[] bytes)
    {
        bool looksUtf16 = bytes.Length > 1 && bytes[1] == 0;
        if (!looksUtf16)
        {
            return Array.IndexOf(bytes, (byte)0) >= 0;
        }
        for (int index = 0; index + 1 < bytes.Length; index += 2)
        {
            if (bytes[index] == 0 && bytes[index + 1] == 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string DecodeNativeString(byte[] bytes)
    {
        bool looksUtf16 = bytes.Length > 1 && bytes[1] == 0;
        if (looksUtf16)
        {
            int end = 0;
            while (end + 1 < bytes.Length
                   && (bytes[end] != 0 || bytes[end + 1] != 0))
            {
                end += 2;
            }
            return Encoding.Unicode.GetString(bytes, 0, end);
        }

        int narrowEnd = Array.IndexOf(bytes, (byte)0);
        if (narrowEnd < 0)
        {
            narrowEnd = bytes.Length;
        }
        return Encoding.UTF8.GetString(bytes, 0, narrowEnd);
    }

    public bool TryReadHashLinkString(
        long stringObject,
        out string value,
        int maximumCharacters = 4096)
    {
        value = string.Empty;
        if (!TryReadBytes(stringObject, 24, out byte[] header))
        {
            return false;
        }

        long type = BitConverter.ToInt64(header, 0);
        long characters = BitConverter.ToInt64(header, 8);
        int length = BitConverter.ToInt32(header, 16);
        if (!IsUserAddress(type)
            || !IsUserAddress(characters)
            || length < 0
            || length > maximumCharacters)
        {
            return false;
        }
        if (length == 0)
        {
            return true;
        }
        if (!TryReadBytes(characters, checked(length * 2), out byte[] bytes))
        {
            return false;
        }

        value = Encoding.Unicode.GetString(bytes);
        return true;
    }

    public bool TryGetHashLinkTypeTable(
        out long typesAddress,
        out int typeCount)
    {
        if (!hashLinkTypeTableLocated)
        {
            hashLinkTypeTableLocated = true;
            LocateHashLinkTypeTable();
        }
        typesAddress = hashLinkTypes;
        typeCount = hashLinkTypeCount;
        return typesAddress != 0 && typeCount > 0;
    }

    public IReadOnlyList<long> FindPattern(
        byte[] pattern,
        bool writableOnly,
        int maximumResults)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length == 0)
        {
            throw new ArgumentException("Pattern is required.", nameof(pattern));
        }
        if (writableOnly && maximumResults == 1)
        {
            long first = FindFirstPatternParallel(pattern);
            return first == 0 ? Array.Empty<long>() : [first];
        }

        var results = new List<long>();
        foreach (MemoryRegion region in EnumerateRegions(writableOnly))
        {
            long offset = 0;
            byte[] overlap = Array.Empty<byte>();
            while (offset < region.Size && results.Count < maximumResults)
            {
                int requested = (int)Math.Min(ChunkSize, region.Size - offset);
                if (!TryReadBytes(region.BaseAddress + offset, requested, out byte[] chunk))
                {
                    offset += requested;
                    overlap = Array.Empty<byte>();
                    continue;
                }

                byte[] searchable;
                long searchableAddress;
                if (overlap.Length == 0)
                {
                    searchable = chunk;
                    searchableAddress = region.BaseAddress + offset;
                }
                else
                {
                    searchable = new byte[overlap.Length + chunk.Length];
                    Buffer.BlockCopy(overlap, 0, searchable, 0, overlap.Length);
                    Buffer.BlockCopy(chunk, 0, searchable, overlap.Length, chunk.Length);
                    searchableAddress = region.BaseAddress + offset - overlap.Length;
                }

                for (int index = IndexOf(searchable, pattern, 0);
                     index >= 0;
                     index = IndexOf(searchable, pattern, index + 1))
                {
                    results.Add(searchableAddress + index);
                    if (results.Count >= maximumResults)
                    {
                        break;
                    }
                }

                int overlapLength = Math.Min(pattern.Length - 1, chunk.Length);
                overlap = chunk[^overlapLength..];
                offset += requested;
            }

            if (results.Count >= maximumResults)
            {
                break;
            }
        }

        return results;
    }

    private long FindFirstPatternParallel(byte[] pattern)
    {
        MemoryRegion[] regions = EnumerateRegions(writableOnly: true).ToArray();
        long foundAddress = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(
                Math.Max(1, Environment.ProcessorCount),
                8),
        };
        Parallel.ForEach(regions, options, (region, loop) =>
        {
            if (Interlocked.Read(ref foundAddress) != 0)
            {
                loop.Stop();
                return;
            }

            long offset = 0;
            byte[] overlap = Array.Empty<byte>();
            while (offset < region.Size
                   && Interlocked.Read(ref foundAddress) == 0)
            {
                int requested = (int)Math.Min(ChunkSize, region.Size - offset);
                if (!TryReadBytes(
                        region.BaseAddress + offset,
                        requested,
                        out byte[] chunk))
                {
                    offset += requested;
                    overlap = Array.Empty<byte>();
                    continue;
                }

                byte[] searchable;
                long searchableAddress;
                if (overlap.Length == 0)
                {
                    searchable = chunk;
                    searchableAddress = region.BaseAddress + offset;
                }
                else
                {
                    searchable = new byte[overlap.Length + chunk.Length];
                    Buffer.BlockCopy(
                        overlap,
                        0,
                        searchable,
                        0,
                        overlap.Length);
                    Buffer.BlockCopy(
                        chunk,
                        0,
                        searchable,
                        overlap.Length,
                        chunk.Length);
                    searchableAddress =
                        region.BaseAddress + offset - overlap.Length;
                }

                int index = IndexOf(searchable, pattern, 0);
                if (index >= 0)
                {
                    long candidate = searchableAddress + index;
                    if (Interlocked.CompareExchange(
                            ref foundAddress,
                            candidate,
                            0) == 0)
                    {
                        loop.Stop();
                    }
                    return;
                }

                int overlapLength =
                    Math.Min(pattern.Length - 1, chunk.Length);
                overlap = chunk[^overlapLength..];
                offset += requested;
            }
        });
        return Interlocked.Read(ref foundAddress);
    }

    public IReadOnlyList<long> FindPointer(
        long pointer,
        bool writableOnly,
        int maximumResults) =>
        ((IProcessMemory)this).FindPointerReferences(
                new HashSet<long> { pointer },
                writableOnly,
                maximumResults)
            .Select(reference => reference.Address)
            .ToArray();

    IReadOnlyList<PointerReference> IProcessMemory.FindPointerReferences(
        IReadOnlySet<long> pointers,
        bool writableOnly,
        int maximumResults)
    {
        if (pointers.Count == 0 || maximumResults <= 0)
        {
            return Array.Empty<PointerReference>();
        }

        var results = new List<PointerReference>();
        foreach (MemoryRegion region in EnumerateRegions(writableOnly))
        {
            long offset = 0;
            while (offset < region.Size && results.Count < maximumResults)
            {
                int requested = (int)Math.Min(ChunkSize, region.Size - offset);
                long chunkAddress = region.BaseAddress + offset;
                if (!TryReadBytes(chunkAddress, requested, out byte[] chunk))
                {
                    offset += requested;
                    continue;
                }

                int alignment = (int)((8 - (chunkAddress & 7)) & 7);
                for (int index = alignment;
                     index + sizeof(long) <= chunk.Length;
                     index += sizeof(long))
                {
                    long target = BinaryPrimitives.ReadInt64LittleEndian(
                        chunk.AsSpan(index, sizeof(long)));
                    if (!pointers.Contains(target))
                    {
                        continue;
                    }
                    results.Add(new PointerReference(
                        target,
                        chunkAddress + index));
                    if (results.Count >= maximumResults)
                    {
                        break;
                    }
                }
                offset += requested;
            }
            if (results.Count >= maximumResults)
            {
                break;
            }
        }
        return results;
    }

    IReadOnlyList<PointerReference> IProcessMemory.FindFirstPointerReferences(
        IReadOnlySet<long> pointers,
        bool writableOnly)
    {
        if (pointers.Count == 0)
        {
            return Array.Empty<PointerReference>();
        }

        var results = new List<PointerReference>();
        var remaining = pointers.ToHashSet();
        foreach (MemoryRegion region in EnumerateRegions(writableOnly))
        {
            long offset = 0;
            while (offset < region.Size && remaining.Count > 0)
            {
                int requested = (int)Math.Min(ChunkSize, region.Size - offset);
                long chunkAddress = region.BaseAddress + offset;
                if (!TryReadBytes(chunkAddress, requested, out byte[] chunk))
                {
                    offset += requested;
                    continue;
                }

                int alignment = (int)((8 - (chunkAddress & 7)) & 7);
                for (int index = alignment;
                     index + sizeof(long) <= chunk.Length;
                     index += sizeof(long))
                {
                    long target = BinaryPrimitives.ReadInt64LittleEndian(
                        chunk.AsSpan(index, sizeof(long)));
                    if (!remaining.Remove(target))
                    {
                        continue;
                    }
                    results.Add(new PointerReference(
                        target,
                        chunkAddress + index));
                    if (remaining.Count == 0)
                    {
                        break;
                    }
                }
                offset += requested;
            }
            if (remaining.Count == 0)
            {
                break;
            }
        }
        return results;
    }

    public bool TryFindPointer(
        long pointer,
        bool writableOnly,
        bool largestRegionsFirst,
        Func<long, bool> predicate,
        out long address)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        address = 0;
        IEnumerable<MemoryRegion> orderedRegions = largestRegionsFirst
            ? EnumerateRegions(writableOnly)
                .OrderByDescending(region => region.Size)
            : EnumerateRegions(writableOnly);
        MemoryRegion[] regions = orderedRegions.ToArray();
        long foundAddress = 0;
        var predicateGate = new object();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(
                Math.Max(1, Environment.ProcessorCount),
                8),
        };
        Parallel.ForEach(regions, options, (region, loop) =>
        {
            if (Interlocked.Read(ref foundAddress) != 0)
            {
                loop.Stop();
                return;
            }
            long offset = 0;
            while (offset < region.Size
                   && Interlocked.Read(ref foundAddress) == 0)
            {
                int requested = (int)Math.Min(ChunkSize, region.Size - offset);
                long chunkAddress = region.BaseAddress + offset;
                if (!TryReadBytes(chunkAddress, requested, out byte[] chunk))
                {
                    offset += requested;
                    continue;
                }

                int alignment = (int)((8 - (chunkAddress & 7)) & 7);
                for (int index = alignment;
                     index + sizeof(long) <= chunk.Length;
                     index += sizeof(long))
                {
                    long target = BinaryPrimitives.ReadInt64LittleEndian(
                        chunk.AsSpan(index, sizeof(long)));
                    if (target != pointer)
                    {
                        continue;
                    }
                    long candidate = chunkAddress + index;
                    lock (predicateGate)
                    {
                        if (foundAddress == 0 && predicate(candidate))
                        {
                            Interlocked.Exchange(
                                ref foundAddress,
                                candidate);
                            loop.Stop();
                            return;
                        }
                    }
                }
                offset += requested;
            }
        });
        address = Interlocked.Read(ref foundAddress);
        return address != 0;
    }

    public static bool IsUserAddress(long address) =>
        address >= 0x10000 && address <= MaximumUserAddress;

    public ProcessMemoryScanSummary GetScanSummary(bool writableOnly)
    {
        IReadOnlyList<MemoryRegion> regions = EnumerateRegions(writableOnly);
        return new ProcessMemoryScanSummary(
            regions.Count,
            regions.Sum(region => region.Size),
            regions.Count == 0 ? 0 : regions.Max(region => region.Size),
            regions.Count(region => region.Size <= 1024 * 1024),
            regions.Where(region => region.Size <= 1024 * 1024)
                .Sum(region => region.Size),
            regions.Count(region =>
                region.Size > 1024 * 1024
                && region.Size <= 16 * 1024 * 1024),
            regions.Where(region =>
                    region.Size > 1024 * 1024
                    && region.Size <= 16 * 1024 * 1024)
                .Sum(region => region.Size),
            regions.Count(region => region.Size > 16 * 1024 * 1024),
            regions.Where(region => region.Size > 16 * 1024 * 1024)
                .Sum(region => region.Size));
    }

    public void Dispose() => handle.Dispose();

    private void LocateHashLinkTypeTable()
    {
        const int blockSize = 4 * 1024;
        for (int moduleOffset = 0;
             moduleOffset < mainModuleSize;
             moduleOffset += blockSize)
        {
            int count = Math.Min(blockSize, mainModuleSize - moduleOffset);
            long blockAddress = mainModuleBase + moduleOffset;
            if (!TryReadBytes(blockAddress, count, out byte[] block))
            {
                continue;
            }

            int alignment = (int)((8 - (blockAddress & 7)) & 7);
            for (int index = alignment;
                 index + sizeof(long) <= block.Length;
                 index += sizeof(long))
            {
                long context = BinaryPrimitives.ReadInt64LittleEndian(
                    block.AsSpan(index, sizeof(long)));
                if (TryReadMainContext(
                        context,
                        out long types,
                        out int countTypes))
                {
                    hashLinkTypes = types;
                    hashLinkTypeCount = countTypes;
                    return;
                }
            }
        }
    }

    private bool TryReadMainContext(
        long context,
        out long types,
        out int typeCount)
    {
        types = 0;
        typeCount = 0;
        if (!IsUserAddress(context)
            || !TryReadBytes(context, 24, out byte[] contextHeader))
        {
            return false;
        }

        long file = BitConverter.ToInt64(contextHeader, 0);
        long code = BitConverter.ToInt64(contextHeader, 8);
        long module = BitConverter.ToInt64(contextHeader, 16);
        if (!IsUserAddress(file)
            || !IsUserAddress(code)
            || !IsUserAddress(module)
            || !TryReadPointer(module, out long moduleCode)
            || moduleCode != code
            || !TryReadBytes(code, 136, out byte[] codeHeader))
        {
            return false;
        }

        string? fileName = ReadNativeString(file, 260 * 2);
        int version = BitConverter.ToInt32(codeHeader, 0);
        int candidateCount = BitConverter.ToInt32(codeHeader, 20);
        long candidateTypes = BitConverter.ToInt64(codeHeader, 128);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith(
                "hlboot.dat",
                StringComparison.OrdinalIgnoreCase)
            || version is < 1 or > 32
            || candidateCount is < 1 or > 1_000_000
            || !IsUserAddress(candidateTypes)
            || !TryReadInt32(candidateTypes, out int firstKind)
            || !Enum.IsDefined(typeof(HashLinkTypeKind), firstKind))
        {
            return false;
        }

        types = candidateTypes;
        typeCount = candidateCount;
        return true;
    }

    private IReadOnlyList<MemoryRegion> EnumerateRegions(bool writableOnly)
    {
        var regions = new List<MemoryRegion>();
        long address = 0x10000;
        int structureSize = System.Runtime.InteropServices.Marshal.SizeOf<
            NativeMethods.MemoryBasicInformation>();
        while (address > 0 && address < MaximumUserAddress)
        {
            IntPtr queried = NativeMethods.VirtualQueryEx(
                handle,
                new IntPtr(address),
                out NativeMethods.MemoryBasicInformation information,
                new IntPtr(structureSize));
            if (queried == IntPtr.Zero)
            {
                break;
            }

            long baseAddress = information.BaseAddress.ToInt64();
            long size = information.RegionSize;
            bool readable = information.State == NativeMethods.MemCommit
                && size > 0
                && (information.Protect
                    & (NativeMethods.PageNoAccess | NativeMethods.PageGuard)) == 0;
            bool writable = (information.Protect & NativeMethods.WritableMask) != 0;
            bool eligible = !writableOnly
                || (writable && information.Type == NativeMethods.MemPrivate);
            if (readable && eligible)
            {
                regions.Add(new MemoryRegion(baseAddress, size));
            }

            long next = baseAddress + size;
            if (next <= address)
            {
                break;
            }
            address = next;
        }
        return writableOnly
            ? regions
                .OrderByDescending(region => region.Size)
                .ThenBy(region => region.BaseAddress)
                .ToArray()
            : regions;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        int relative = haystack.AsSpan(start).IndexOf(needle);
        return relative < 0 ? -1 : start + relative;
    }

    private sealed record MemoryRegion(long BaseAddress, long Size);
}

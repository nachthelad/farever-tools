using System.ComponentModel;
using System.Buffers.Binary;
using System.Text;

namespace FareverTranslate.Core.Memory;

public sealed class ProcessMemory : IProcessMemory
{
    private const int ChunkSize = 4 * 1024 * 1024;
    private const long MaximumUserAddress = 0x00007FFFFFFEFFFF;
    private readonly NativeMethods.SafeProcessMemoryHandle handle;

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
        if (!TryReadBytes(address, maximumBytes, out byte[] bytes))
        {
            return null;
        }

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

    public IReadOnlyList<long> FindPointer(
        long pointer,
        bool writableOnly,
        int maximumResults) =>
        FindPattern(BitConverter.GetBytes(pointer), writableOnly, maximumResults)
            .Where(address => (address & 7) == 0)
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

    public static bool IsUserAddress(long address) =>
        address >= 0x10000 && address <= MaximumUserAddress;

    public void Dispose() => handle.Dispose();

    private IEnumerable<MemoryRegion> EnumerateRegions(bool writableOnly)
    {
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
                yield break;
            }

            long baseAddress = information.BaseAddress.ToInt64();
            long size = information.RegionSize;
            bool readable = information.State == NativeMethods.MemCommit
                && size > 0
                && (information.Protect
                    & (NativeMethods.PageNoAccess | NativeMethods.PageGuard)) == 0;
            bool writable = (information.Protect & NativeMethods.WritableMask) != 0;
            if (readable && (!writableOnly || writable))
            {
                yield return new MemoryRegion(baseAddress, size);
            }

            long next = baseAddress + size;
            if (next <= address)
            {
                yield break;
            }
            address = next;
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        int relative = haystack.AsSpan(start).IndexOf(needle);
        return relative < 0 ? -1 : start + relative;
    }

    private sealed record MemoryRegion(long BaseAddress, long Size);
}

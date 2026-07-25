using System.Text;

namespace FareverTranslate.Core.Memory;

public enum HashLinkTypeKind
{
    Void = 0,
    UInt8 = 1,
    UInt16 = 2,
    Int32 = 3,
    Int64 = 4,
    Float32 = 5,
    Float64 = 6,
    Bool = 7,
    Bytes = 8,
    Dynamic = 9,
    Function = 10,
    Object = 11,
    Array = 12,
    Type = 13,
    Reference = 14,
    Virtual = 15,
    DynamicObject = 16,
    Abstract = 17,
    Enum = 18,
    Null = 19,
    Method = 20,
    Struct = 21,
}

public sealed record ResolvedHashLinkField(
    string Name,
    int Offset,
    long TypeAddress,
    HashLinkTypeKind TypeKind);

public sealed class ResolvedHashLinkType
{
    internal ResolvedHashLinkType(
        long typeAddress,
        long typeObjectAddress,
        string name,
        IReadOnlyDictionary<string, ResolvedHashLinkField> fields)
    {
        TypeAddress = typeAddress;
        TypeObjectAddress = typeObjectAddress;
        Name = name;
        Fields = fields;
    }

    public long TypeAddress { get; }
    public long TypeObjectAddress { get; }
    public string Name { get; }
    public IReadOnlyDictionary<string, ResolvedHashLinkField> Fields { get; }
}

public sealed class HashLinkMetadataResolver
{
    private readonly IProcessMemory memory;
    private readonly Dictionary<long, ResolvedHashLinkType> byAddress = new();

    internal HashLinkMetadataResolver(IProcessMemory memory)
    {
        this.memory = memory;
    }

    public ResolvedHashLinkType? Resolve(string requestedName)
    {
        IReadOnlyDictionary<string, ResolvedHashLinkType> resolved =
            ResolveMany([requestedName]);
        return resolved.GetValueOrDefault(requestedName);
    }

    public IReadOnlyDictionary<string, ResolvedHashLinkType> ResolveMany(
        IReadOnlyCollection<string> requestedNames)
    {
        var requested = requestedNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length == 0)
        {
            return new Dictionary<string, ResolvedHashLinkType>();
        }

        var namesByAddress = new Dictionary<long, string>();
        foreach (string requestedName in requested)
        {
            foreach (byte[] namePattern in new[]
            {
                Encoding.Unicode.GetBytes(requestedName + "\0"),
                Encoding.UTF8.GetBytes(requestedName + "\0"),
            })
            {
                foreach (long nameAddress in memory.FindPattern(
                             namePattern,
                             writableOnly: false,
                             maximumResults: 64))
                {
                    namesByAddress.TryAdd(nameAddress, requestedName);
                }
            }
        }
        if (namesByAddress.Count == 0)
        {
            return new Dictionary<string, ResolvedHashLinkType>();
        }

        var typeObjectNames = new Dictionary<long, string>();
        foreach (PointerReference reference in memory.FindPointerReferences(
                     namesByAddress.Keys.ToHashSet(),
                     writableOnly: false,
                     maximumResults: 4096))
        {
            string requestedName = namesByAddress[reference.Target];
            long typeObject = reference.Address - 16;
            if (ValidateTypeObject(typeObject, requestedName))
            {
                typeObjectNames.TryAdd(typeObject, requestedName);
            }
        }
        if (typeObjectNames.Count == 0)
        {
            return new Dictionary<string, ResolvedHashLinkType>();
        }

        var result = new Dictionary<string, ResolvedHashLinkType>(
            StringComparer.Ordinal);
        foreach (PointerReference reference in memory.FindPointerReferences(
                     typeObjectNames.Keys.ToHashSet(),
                     writableOnly: false,
                     maximumResults: 4096))
        {
            long typeAddress = reference.Address - 8;
            ResolvedHashLinkType? resolved = ResolveByAddress(typeAddress);
            string requestedName = typeObjectNames[reference.Target];
            if (resolved is not null
                && string.Equals(
                    resolved.Name,
                    requestedName,
                    StringComparison.Ordinal))
            {
                result.TryAdd(requestedName, resolved);
            }
        }
        return result;
    }

    public ResolvedHashLinkType? ResolveByAddress(long typeAddress)
    {
        if (byAddress.TryGetValue(typeAddress, out ResolvedHashLinkType? cached))
        {
            return cached;
        }
        if (!memory.TryReadBytes(typeAddress, 16, out byte[] typeBytes)
            || BitConverter.ToInt32(typeBytes, 0) != (int)HashLinkTypeKind.Object)
        {
            return null;
        }

        long typeObject = BitConverter.ToInt64(typeBytes, 8);
        string? name = ReadObjectTypeName(typeObject);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        IReadOnlyList<RawField> rawFields = CollectFields(
            typeObject,
            depth: 0);
        if (!memory.TryReadPointer(typeObject + 72, out long runtime)
            || !memory.TryReadInt32(runtime + 8, out int runtimeFields)
            || !memory.TryReadPointer(runtime + 40, out long indexes)
            || runtimeFields <= 0
            || runtimeFields > 8192
            || rawFields.Count != runtimeFields)
        {
            return null;
        }

        var fields = new Dictionary<string, ResolvedHashLinkField>(
            StringComparer.Ordinal);
        for (int index = 0; index < rawFields.Count; index++)
        {
            if (!memory.TryReadInt32(indexes + index * 4L, out int offset)
                || offset < 8
                || offset >= 1_000_000)
            {
                continue;
            }
            RawField raw = rawFields[index];
            fields[raw.Name] = new ResolvedHashLinkField(
                raw.Name,
                offset,
                raw.TypeAddress,
                ReadKind(raw.TypeAddress));
        }

        if (fields.Count == 0)
        {
            return null;
        }

        var result = new ResolvedHashLinkType(
            typeAddress,
            typeObject,
            name,
            fields);
        byAddress[typeAddress] = result;
        return result;
    }

    public HashLinkTypeKind ReadKind(long typeAddress)
    {
        if (!memory.TryReadInt32(typeAddress, out int kind)
            || !Enum.IsDefined(typeof(HashLinkTypeKind), kind))
        {
            return HashLinkTypeKind.Void;
        }
        return (HashLinkTypeKind)kind;
    }

    public string? TryReadEnumName(long enumValue)
    {
        if (!memory.TryReadPointer(enumValue, out long typeAddress)
            || ReadKind(typeAddress) != HashLinkTypeKind.Enum
            || !memory.TryReadInt32(enumValue + 8, out int index)
            || !memory.TryReadPointer(typeAddress + 8, out long enumType)
            || !memory.TryReadInt32(enumType + 8, out int count)
            || !memory.TryReadPointer(enumType + 16, out long constructs)
            || index < 0
            || index >= count
            || count > 4096)
        {
            return null;
        }

        const int constructSize = 40;
        if (!memory.TryReadPointer(
                constructs + index * constructSize,
                out long nameAddress))
        {
            return null;
        }
        return memory.ReadNativeString(nameAddress);
    }

    private IReadOnlyList<RawField> CollectFields(long typeObject, int depth)
    {
        if (depth > 64)
        {
            throw new InvalidOperationException(
                "La herencia HashLink es inesperadamente profunda.");
        }

        var result = new List<RawField>();
        if (memory.TryReadPointer(typeObject + 24, out long superType)
            && superType > 0
            && memory.TryReadPointer(superType + 8, out long superObject))
        {
            result.AddRange(CollectFields(superObject, depth + 1));
        }

        if (!memory.TryReadInt32(typeObject, out int count)
            || !memory.TryReadPointer(typeObject + 32, out long fields)
            || count < 0
            || count > 4096)
        {
            return result;
        }

        for (int index = 0; index < count; index++)
        {
            long field = fields + index * 24L;
            if (!memory.TryReadPointer(field, out long nameAddress)
                || !memory.TryReadPointer(field + 8, out long fieldType))
            {
                continue;
            }
            string? name = memory.ReadNativeString(nameAddress);
            if (!string.IsNullOrEmpty(name))
            {
                result.Add(new RawField(name, fieldType));
            }
        }
        return result;
    }

    private string? ReadObjectTypeName(long typeObject)
    {
        if (!memory.TryReadBytes(typeObject, 80, out byte[] bytes))
        {
            return null;
        }
        int ownFields = BitConverter.ToInt32(bytes, 0);
        long nameAddress = BitConverter.ToInt64(bytes, 16);
        long runtime = BitConverter.ToInt64(bytes, 72);
        if (ownFields < 0
            || ownFields >= 4096
            || !ProcessMemory.IsUserAddress(runtime))
        {
            return null;
        }
        return memory.ReadNativeString(nameAddress);
    }

    private bool ValidateTypeObject(long typeObject, string requestedName) =>
        string.Equals(
            ReadObjectTypeName(typeObject),
            requestedName,
            StringComparison.Ordinal);

    private sealed record RawField(string Name, long TypeAddress);
}

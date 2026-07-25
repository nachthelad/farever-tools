using System.Text;
using FareverTranslate.Core.Memory;

namespace FareverTranslate.Tests;

internal sealed class SyntheticHashLinkMemory : IProcessMemory
{
    private const long BaseAddress = 0x10000000;
    private readonly byte[] bytes = new byte[2 * 1024 * 1024];
    private int cursor = 0x1000;

    internal long Allocate(int count)
    {
        cursor = (cursor + 15) & ~15;
        if (cursor + count > bytes.Length)
        {
            throw new InvalidOperationException("Memoria sintética agotada.");
        }
        long address = BaseAddress + cursor;
        cursor += count;
        return address;
    }

    internal long NativeString(string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value + "\0");
        long address = Allocate(Math.Max(512, encoded.Length));
        Write(address, encoded);
        return address;
    }

    internal long HashLinkString(string value, long stringType)
    {
        byte[] characters = Encoding.Unicode.GetBytes(value);
        long characterAddress = Allocate(Math.Max(2, characters.Length));
        if (characters.Length > 0)
        {
            Write(characterAddress, characters);
        }
        long header = Allocate(24);
        WriteInt64(header, stringType);
        WriteInt64(header + 8, characterAddress);
        WriteInt32(header + 16, value.Length);
        return header;
    }

    internal long PrimitiveType(HashLinkTypeKind kind)
    {
        long type = Allocate(16);
        WriteInt32(type, (int)kind);
        return type;
    }

    internal long ObjectType(
        string name,
        params (string Name, long Type, int Offset)[] fields)
    {
        long fieldTable = Allocate(Math.Max(1, fields.Length) * 24);
        long indexes = Allocate(Math.Max(1, fields.Length) * 4);
        for (int index = 0; index < fields.Length; index++)
        {
            WriteInt64(fieldTable + index * 24L, NativeString(fields[index].Name));
            WriteInt64(fieldTable + index * 24L + 8, fields[index].Type);
            WriteInt32(indexes + index * 4L, fields[index].Offset);
        }

        long runtime = Allocate(48);
        WriteInt32(runtime + 8, fields.Length);
        WriteInt64(runtime + 40, indexes);

        long typeObject = Allocate(80);
        WriteInt32(typeObject, fields.Length);
        WriteInt64(typeObject + 16, NativeString(name));
        WriteInt64(typeObject + 32, fieldTable);
        WriteInt64(typeObject + 72, runtime);

        long type = Allocate(16);
        WriteInt32(type, (int)HashLinkTypeKind.Object);
        WriteInt64(type + 8, typeObject);
        return type;
    }

    internal long Object(long type, int size = 64)
    {
        long instance = Allocate(size);
        WriteInt64(instance, type);
        return instance;
    }

    internal long PointerArray(long arrayType, long elementType, params long[] values)
    {
        long array = Allocate(24 + values.Length * 8);
        WriteInt64(array, arrayType);
        WriteInt64(array + 8, elementType);
        WriteInt32(array + 16, values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            WriteInt64(array + 24 + index * 8L, values[index]);
        }
        return array;
    }

    internal void WriteByte(long address, byte value) =>
        bytes[Offset(address)] = value;

    internal void WriteInt32(long address, int value) =>
        Write(address, BitConverter.GetBytes(value));

    internal void WriteInt64(long address, long value) =>
        Write(address, BitConverter.GetBytes(value));

    public bool TryReadBytes(long address, int count, out byte[] buffer)
    {
        buffer = Array.Empty<byte>();
        int offset = OffsetOrInvalid(address);
        if (offset < 0 || count <= 0 || offset + count > cursor)
        {
            return false;
        }
        buffer = bytes.AsSpan(offset, count).ToArray();
        return true;
    }

    public bool TryReadPointer(long address, out long value)
    {
        value = 0;
        if (!TryReadBytes(address, 8, out byte[] data))
        {
            return false;
        }
        value = BitConverter.ToInt64(data);
        return true;
    }

    public bool TryReadInt32(long address, out int value)
    {
        value = 0;
        if (!TryReadBytes(address, 4, out byte[] data))
        {
            return false;
        }
        value = BitConverter.ToInt32(data);
        return true;
    }

    public bool TryReadByte(long address, out byte value)
    {
        value = 0;
        int offset = OffsetOrInvalid(address);
        if (offset < 0 || offset >= cursor)
        {
            return false;
        }
        value = bytes[offset];
        return true;
    }

    public string? ReadNativeString(long address, int maximumBytes = 512)
    {
        int offset = OffsetOrInvalid(address);
        if (offset < 0 || offset >= cursor)
        {
            return null;
        }
        int count = Math.Min(maximumBytes, cursor - offset);
        ReadOnlySpan<byte> source = bytes.AsSpan(offset, count);
        bool utf16 = source.Length > 1 && source[1] == 0;
        if (utf16)
        {
            int end = 0;
            while (end + 1 < source.Length
                   && (source[end] != 0 || source[end + 1] != 0))
            {
                end += 2;
            }
            return Encoding.Unicode.GetString(source[..end]);
        }
        int narrowEnd = source.IndexOf((byte)0);
        narrowEnd = narrowEnd < 0 ? source.Length : narrowEnd;
        return Encoding.UTF8.GetString(source[..narrowEnd]);
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
        byte[] content = Array.Empty<byte>();
        if (!ProcessMemory.IsUserAddress(type)
            || length < 0
            || length > maximumCharacters)
        {
            return false;
        }
        if (length > 0
            && !TryReadBytes(characters, length * 2, out content))
        {
            return false;
        }
        if (length > 0)
        {
            value = Encoding.Unicode.GetString(content);
        }
        return true;
    }

    public IReadOnlyList<long> FindPattern(
        byte[] pattern,
        bool writableOnly,
        int maximumResults)
    {
        var results = new List<long>();
        int start = 0;
        ReadOnlySpan<byte> used = bytes.AsSpan(0, cursor);
        while (start <= used.Length - pattern.Length
               && results.Count < maximumResults)
        {
            int relative = used[start..].IndexOf(pattern);
            if (relative < 0)
            {
                break;
            }
            int found = start + relative;
            results.Add(BaseAddress + found);
            start = found + 1;
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

    public IReadOnlyList<PointerReference> FindPointerReferences(
        IReadOnlySet<long> pointers,
        bool writableOnly,
        int maximumResults)
    {
        var results = new List<PointerReference>();
        for (int offset = 0;
             offset + sizeof(long) <= cursor && results.Count < maximumResults;
             offset += sizeof(long))
        {
            long target = BitConverter.ToInt64(bytes, offset);
            if (pointers.Contains(target))
            {
                results.Add(new PointerReference(
                    target,
                    BaseAddress + offset));
            }
        }
        return results;
    }

    public void Dispose()
    {
    }

    private void Write(long address, byte[] value)
    {
        int offset = Offset(address);
        value.CopyTo(bytes, offset);
    }

    private static int Offset(long address)
    {
        int result = checked((int)(address - BaseAddress));
        if (result < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(address));
        }
        return result;
    }

    private static int OffsetOrInvalid(long address)
    {
        long offset = address - BaseAddress;
        return offset is < 0 or > int.MaxValue ? -1 : (int)offset;
    }
}

internal sealed class SyntheticChatFixture
{
    private readonly long arrayType;
    private readonly long messageType;
    private readonly long chatType;
    private readonly long stringType;

    internal SyntheticChatFixture()
    {
        Memory = new SyntheticHashLinkMemory();
        long boolType = Memory.PrimitiveType(HashLinkTypeKind.Bool);
        stringType = Memory.PrimitiveType(HashLinkTypeKind.Dynamic);
        arrayType = Memory.PrimitiveType(HashLinkTypeKind.Array);
        messageType = Memory.ObjectType(
            "st.chat.Message",
            ("text", stringType, 8),
            ("sender", stringType, 16),
            ("channel", stringType, 24));
        chatType = Memory.ObjectType(
            "st.player.ChatClient",
            ("history", arrayType, 8));
        PlayerType = Memory.ObjectType(
            "st.Player",
            ("isMe", boolType, 8),
            ("chatClient", chatType, 16),
            ("name", stringType, 24));

        PortugueseMessage = CreateMessage(
            "João",
            "Geral",
            "Vocês vão para a floresta amanhã?");
        long duplicate = PortugueseMessage;
        long selfMessage = CreateMessage("local", "Geral", "minha mensagem");
        Player = Memory.Object(PlayerType);
        Memory.WriteByte(Player + 8, 1);
        Memory.WriteInt64(
            Player + 24,
            Memory.HashLinkString("local", stringType));
        Memory.WriteInt64(selfMessage + 16, Player);
        long systemMessage = CreateMessage(
            "Servidor",
            "System",
            "maintenance");
        long history = Memory.PointerArray(
            arrayType,
            messageType,
            PortugueseMessage,
            duplicate,
            0x20000000,
            selfMessage,
            systemMessage);
        CurrentChat = CreateChat(history);
        Memory.WriteInt64(Player + 16, CurrentChat);
    }

    internal SyntheticHashLinkMemory Memory { get; }
    internal long PlayerType { get; }
    internal long Player { get; }
    internal long PortugueseMessage { get; }
    internal long CurrentChat { get; private set; }

    internal void ReconnectWith(string text)
    {
        long message = CreateMessage("Maria", "Grupo", text);
        long history = Memory.PointerArray(arrayType, messageType, message);
        CurrentChat = CreateChat(history);
        Memory.WriteInt64(Player + 16, CurrentChat);
    }

    private long CreateChat(long history)
    {
        long chat = Memory.Object(chatType);
        Memory.WriteInt64(chat + 8, history);
        return chat;
    }

    private long CreateMessage(string sender, string channel, string text)
    {
        long message = Memory.Object(messageType);
        Memory.WriteInt64(
            message + 8,
            Memory.HashLinkString(text, stringType));
        Memory.WriteInt64(
            message + 16,
            Memory.HashLinkString(sender, stringType));
        Memory.WriteInt64(
            message + 24,
            Memory.HashLinkString(channel, stringType));
        return message;
    }
}

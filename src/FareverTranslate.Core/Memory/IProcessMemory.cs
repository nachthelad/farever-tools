namespace FareverTranslate.Core.Memory;

internal interface IProcessMemory : IDisposable
{
    bool TryReadBytes(long address, int count, out byte[] buffer);
    bool TryReadPointer(long address, out long value);
    bool TryReadInt32(long address, out int value);
    bool TryReadByte(long address, out byte value);
    string? ReadNativeString(long address, int maximumBytes = 512);
    bool TryReadHashLinkString(
        long stringObject,
        out string value,
        int maximumCharacters = 4096);
    IReadOnlyList<long> FindPattern(
        byte[] pattern,
        bool writableOnly,
        int maximumResults);
    IReadOnlyList<long> FindPointer(
        long pointer,
        bool writableOnly,
        int maximumResults);
}

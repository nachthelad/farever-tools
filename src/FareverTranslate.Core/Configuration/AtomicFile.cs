using System.Text;

namespace FareverTranslate.Core.Configuration;

internal static class AtomicFile
{
    internal static void WriteAllText(string path, string content) =>
        WriteAllBytes(path, Encoding.UTF8.GetBytes(content));

    internal static void WriteAllBytes(string path, byte[] content)
    {
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }
}


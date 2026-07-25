namespace FareverTranslate.Core.Configuration;

public sealed class AppPaths
{
    public AppPaths(string? baseDirectory = null)
    {
        Root = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FareverTranslate");
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string CredentialFile => Path.Combine(Root, "credentials.bin");
    public string CacheFile => Path.Combine(Root, "translation-cache.bin");
    public string LogFile => Path.Combine(Root, "farever-translate.log");

    public void EnsureCreated() => Directory.CreateDirectory(Root);
}


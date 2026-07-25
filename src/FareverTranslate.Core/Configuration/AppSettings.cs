using System.Text.Json;

namespace FareverTranslate.Core.Configuration;

public sealed record AppSettings
{
    public double OffsetX { get; init; } = 24;
    public double OffsetFromBottom { get; init; } = 260;
    public bool PanelVisible { get; init; } = true;
    public int MaximumMessages { get; init; } = 6;
    public string TargetLanguage { get; init; } = "es";
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly AppPaths paths;

    public SettingsStore(AppPaths paths)
    {
        this.paths = paths;
    }

    public bool Exists => File.Exists(paths.SettingsFile);

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(paths.SettingsFile))
            {
                return new AppSettings();
            }

            AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(paths.SettingsFile),
                JsonOptions) ?? new AppSettings();
            return Normalize(loaded);
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        paths.EnsureCreated();
        AtomicFile.WriteAllText(
            paths.SettingsFile,
            JsonSerializer.Serialize(Normalize(settings), JsonOptions));
    }

    private static AppSettings Normalize(AppSettings settings) =>
        settings with
        {
            OffsetX = double.IsFinite(settings.OffsetX)
                ? Math.Clamp(settings.OffsetX, -4000, 4000)
                : 24,
            OffsetFromBottom = double.IsFinite(settings.OffsetFromBottom)
                ? Math.Clamp(settings.OffsetFromBottom, -4000, 4000)
                : 260,
            MaximumMessages = 6,
            TargetLanguage = "es",
        };
}

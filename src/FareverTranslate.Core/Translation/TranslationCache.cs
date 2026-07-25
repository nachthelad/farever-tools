using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FareverTranslate.Core.Configuration;
using FareverTranslate.Core.Security;

namespace FareverTranslate.Core.Translation;

public sealed record CachedTranslation(
    string TranslatedText,
    string? DetectedLanguage,
    DateTimeOffset CreatedAt);

public sealed class TranslationCache
{
    private const int MaximumEntries = 500;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private readonly object gate = new();
    private readonly AppPaths paths;
    private Dictionary<string, CachedTranslation> entries;

    public TranslationCache(AppPaths paths)
    {
        this.paths = paths;
        entries = Load();
    }

    public bool TryGet(string sourceText, out CachedTranslation? translation)
    {
        lock (gate)
        {
            string key = Key(sourceText);
            if (!entries.TryGetValue(key, out translation))
            {
                return false;
            }
            if (DateTimeOffset.UtcNow - translation.CreatedAt <= Lifetime)
            {
                return true;
            }

            entries.Remove(key);
            translation = null;
            Save();
            return false;
        }
    }

    public void Put(
        string sourceText,
        string translatedText,
        string? detectedLanguage)
    {
        lock (gate)
        {
            entries[Key(sourceText)] = new CachedTranslation(
                translatedText,
                detectedLanguage,
                DateTimeOffset.UtcNow);

            entries = entries
                .Where(pair => DateTimeOffset.UtcNow - pair.Value.CreatedAt <= Lifetime)
                .OrderByDescending(pair => pair.Value.CreatedAt)
                .Take(MaximumEntries)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Save();
        }
    }

    private Dictionary<string, CachedTranslation> Load()
    {
        try
        {
            if (!File.Exists(paths.CacheFile))
            {
                return new Dictionary<string, CachedTranslation>();
            }

            byte[] json = DpapiProtector.Unprotect(
                File.ReadAllBytes(paths.CacheFile));
            return JsonSerializer.Deserialize<Dictionary<string, CachedTranslation>>(json)
                ?? new Dictionary<string, CachedTranslation>();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or System.ComponentModel.Win32Exception)
        {
            return new Dictionary<string, CachedTranslation>();
        }
    }

    private void Save()
    {
        paths.EnsureCreated();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(entries);
        AtomicFile.WriteAllBytes(paths.CacheFile, DpapiProtector.Protect(json));
    }

    private static string Key(string sourceText)
    {
        string normalized = sourceText.Trim().Normalize(NormalizationForm.FormKC);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}


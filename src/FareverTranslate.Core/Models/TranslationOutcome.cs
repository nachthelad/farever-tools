namespace FareverTranslate.Core.Models;

public enum TranslationErrorKind
{
    None,
    MissingCredential,
    InvalidCredential,
    QuotaExceeded,
    Network,
    Service,
    InvalidResponse,
}

public sealed record TranslationOutcome(
    bool Success,
    string? TranslatedText,
    string? DetectedLanguage,
    TranslationErrorKind ErrorKind,
    string UserMessage)
{
    public static TranslationOutcome Ok(string text, string? detectedLanguage) =>
        new(true, text, detectedLanguage, TranslationErrorKind.None, string.Empty);

    public static TranslationOutcome Error(
        TranslationErrorKind kind,
        string message) =>
        new(false, null, null, kind, message);
}


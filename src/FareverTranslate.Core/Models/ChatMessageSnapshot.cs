namespace FareverTranslate.Core.Models;

public sealed record ChatMessageSnapshot(
    string Identity,
    string Sender,
    string Channel,
    string Text,
    bool IsSelf,
    bool IsSystem);


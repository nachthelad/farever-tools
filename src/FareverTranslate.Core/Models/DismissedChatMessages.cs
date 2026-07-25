namespace FareverTranslate.Core.Models;

internal sealed class DismissedChatMessages
{
    private readonly HashSet<string> identities =
        new(StringComparer.Ordinal);

    internal int Count => identities.Count;

    internal void Dismiss(string identity)
    {
        if (!string.IsNullOrWhiteSpace(identity))
        {
            identities.Add(identity);
        }
    }

    internal IReadOnlyList<ChatMessageSnapshot> Filter(
        IReadOnlyList<ChatMessageSnapshot> snapshots) =>
        snapshots
            .Where(snapshot => !identities.Contains(snapshot.Identity))
            .ToArray();
}

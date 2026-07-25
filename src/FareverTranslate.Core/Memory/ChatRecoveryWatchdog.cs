namespace FareverTranslate.Core.Memory;

internal enum ChatRecoveryAction
{
    None,
    RefreshReferences,
    RelocateObjects,
}

internal sealed class ChatRecoveryWatchdog
{
    private static readonly TimeSpan RefreshInterval =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InitialRelocationDelay =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumRelocationDelay =
        TimeSpan.FromSeconds(60);

    private DateTimeOffset? emptySince;
    private DateTimeOffset nextRefresh;
    private DateTimeOffset nextRelocation;
    private TimeSpan relocationDelay = InitialRelocationDelay;

    internal ChatRecoveryAction Observe(
        DateTimeOffset now,
        bool recognizedMessage)
    {
        if (recognizedMessage)
        {
            Reset();
            return ChatRecoveryAction.None;
        }

        if (emptySince is null)
        {
            emptySince = now;
            nextRefresh = now + RefreshInterval;
            nextRelocation = now + InitialRelocationDelay;
            relocationDelay = InitialRelocationDelay;
            return ChatRecoveryAction.None;
        }

        if (now >= nextRelocation)
        {
            relocationDelay = TimeSpan.FromSeconds(
                Math.Min(
                    MaximumRelocationDelay.TotalSeconds,
                    relocationDelay.TotalSeconds * 2));
            nextRelocation = now + relocationDelay;
            nextRefresh = now + RefreshInterval;
            return ChatRecoveryAction.RelocateObjects;
        }

        if (now >= nextRefresh)
        {
            nextRefresh = now + RefreshInterval;
            return ChatRecoveryAction.RefreshReferences;
        }

        return ChatRecoveryAction.None;
    }

    internal void Reset()
    {
        emptySince = null;
        nextRefresh = default;
        nextRelocation = default;
        relocationDelay = InitialRelocationDelay;
    }
}

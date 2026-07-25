namespace FareverTranslate.Core.Models;

internal readonly record struct AltInteractionSample(
    bool AltDown,
    bool DisqualifyingChordDown,
    bool TargetReady);

internal enum AltInteractionChange
{
    None,
    BecameInteractive,
    BecamePassive,
}

internal sealed class AltInteractionToggle
{
    private bool observedReleased;
    private bool altWasDown;
    private bool pendingSoloAlt;

    internal bool IsInteractive { get; private set; }

    internal AltInteractionChange Update(AltInteractionSample sample)
    {
        if (!sample.TargetReady)
        {
            bool wasInteractive = IsInteractive;
            IsInteractive = false;
            pendingSoloAlt = false;
            observedReleased = !sample.AltDown;
            altWasDown = sample.AltDown;
            return wasInteractive
                ? AltInteractionChange.BecamePassive
                : AltInteractionChange.None;
        }

        if (!observedReleased)
        {
            if (!sample.AltDown)
            {
                observedReleased = true;
                pendingSoloAlt = false;
            }
            altWasDown = sample.AltDown;
            return AltInteractionChange.None;
        }

        if (sample.AltDown && !altWasDown)
        {
            pendingSoloAlt = !sample.DisqualifyingChordDown;
        }
        else if (sample.AltDown
                 && pendingSoloAlt
                 && sample.DisqualifyingChordDown)
        {
            pendingSoloAlt = false;
        }
        else if (!sample.AltDown && altWasDown)
        {
            bool shouldToggle = pendingSoloAlt;
            pendingSoloAlt = false;
            if (shouldToggle)
            {
                IsInteractive = !IsInteractive;
                altWasDown = false;
                return IsInteractive
                    ? AltInteractionChange.BecameInteractive
                    : AltInteractionChange.BecamePassive;
            }
        }

        altWasDown = sample.AltDown;
        return AltInteractionChange.None;
    }
}

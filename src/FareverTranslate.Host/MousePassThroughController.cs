namespace FareverTranslate.Host;

internal sealed class MousePassThroughController
{
    private readonly Func<bool, bool> applyNativeClickThrough;
    private readonly Action<bool> setWpfHitTesting;

    internal MousePassThroughController(
        Func<bool, bool> applyNativeClickThrough,
        Action<bool> setWpfHitTesting)
    {
        this.applyNativeClickThrough = applyNativeClickThrough;
        this.setWpfHitTesting = setWpfHitTesting;
    }

    internal bool SafetyFault { get; private set; }

    internal bool TrySetInteractive(bool interactive)
    {
        if (SafetyFault)
        {
            setWpfHitTesting(false);
            return false;
        }

        if (!interactive)
        {
            setWpfHitTesting(false);
        }

        bool clickThrough = !interactive;
        if (!applyNativeClickThrough(clickThrough))
        {
            SafetyFault = true;
            setWpfHitTesting(false);
            return false;
        }

        if (interactive)
        {
            setWpfHitTesting(true);
        }
        return true;
    }
}

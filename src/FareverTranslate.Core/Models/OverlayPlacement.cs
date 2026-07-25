namespace FareverTranslate.Core.Models;

public readonly record struct OverlayPlacement(double Left, double Top)
{
    public static OverlayPlacement Calculate(
        int gameLeftPixels,
        int gameBottomPixels,
        double dpiScale,
        double panelHeight,
        double offsetX,
        double offsetFromBottom)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale));
        }
        return new OverlayPlacement(
            gameLeftPixels / dpiScale + offsetX,
            gameBottomPixels / dpiScale - offsetFromBottom - panelHeight);
    }
}

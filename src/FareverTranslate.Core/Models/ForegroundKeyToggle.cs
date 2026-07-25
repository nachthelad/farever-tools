namespace FareverTranslate.Core.Models;

public sealed class ForegroundKeyToggle
{
    private bool wasDown;

    public bool Update(bool isDown, bool targetIsForeground)
    {
        bool pressed = isDown && !wasDown && targetIsForeground;
        wasDown = isDown;
        return pressed;
    }
}

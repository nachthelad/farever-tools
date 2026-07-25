using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FareverTranslate.Host;

internal static class NativeWindow
{
    internal const int GwlExStyle = -20;
    internal const long WsExNoActivate = 0x08000000L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const int WmHotKey = 0x0312;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const int HotKeyToggle = 0x4654;

    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        IntPtr window,
        out WindowRect rectangle);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(
        IntPtr window,
        int index,
        IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(
        IntPtr window,
        int identifier,
        uint modifiers,
        uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(
        IntPtr window,
        int identifier);

    internal static void SetNoActivate(
        System.Windows.Window window,
        bool enabled)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        style = enabled
            ? style | WsExNoActivate
            : style & ~WsExNoActivate;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;

    internal long Area =>
        Math.Max(0, Right - Left) * (long)Math.Max(0, Bottom - Top);
}

internal sealed class GameWindowTracker
{
    private readonly uint processId;

    internal GameWindowTracker(int processId)
    {
        this.processId = checked((uint)processId);
    }

    internal bool TryGetWindow(
        out IntPtr window,
        out WindowRect rectangle,
        out double dpiScale,
        out bool minimized)
    {
        IntPtr selectedWindow = IntPtr.Zero;
        WindowRect selectedRectangle = default;
        dpiScale = 1;
        minimized = false;
        long largestArea = 0;

        NativeWindow.EnumWindows((candidate, _) =>
        {
            NativeWindow.GetWindowThreadProcessId(candidate, out uint owner);
            if (owner != processId
                || !NativeWindow.IsWindowVisible(candidate)
                || !NativeWindow.GetWindowRect(candidate, out WindowRect rect)
                || rect.Area <= largestArea)
            {
                return true;
            }

            largestArea = rect.Area;
            selectedWindow = candidate;
            selectedRectangle = rect;
            return true;
        }, IntPtr.Zero);

        window = selectedWindow;
        rectangle = selectedRectangle;
        if (selectedWindow == IntPtr.Zero)
        {
            return false;
        }

        minimized = NativeWindow.IsIconic(selectedWindow);
        uint dpi = NativeWindow.GetDpiForWindow(selectedWindow);
        dpiScale = dpi == 0 ? 1 : dpi / 96d;
        return true;
    }
}

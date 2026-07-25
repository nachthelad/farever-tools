using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FareverTranslate.Host;

internal static class NativeWindow
{
    internal const int GwlExStyle = -20;
    internal const int GwlHwndParent = -8;
    internal const long WsExNoActivate = 0x08000000L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExTransparent = 0x00000020L;
    internal const int WmMouseActivate = 0x0021;
    internal const int MaNoActivate = 3;
    private const int VirtualKeyF6 = 0x75;
    private const int VirtualKeyAlt = 0x12;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyTab = 0x09;
    private const int VirtualKeyEnter = 0x0D;
    private const int VirtualKeyF4 = 0x73;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int HtBottomRight = 17;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

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

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(
        IntPtr window,
        int index,
        IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(
        IntPtr window,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal static bool IsF6Down() =>
        IsKeyDown(VirtualKeyF6);

    internal static bool IsAltDown() =>
        IsKeyDown(VirtualKeyAlt);

    internal static bool IsDisqualifyingAltChordDown() =>
        IsDisqualifyingAltChord(
            IsKeyDown(VirtualKeyControl),
            IsKeyDown(VirtualKeyShift),
            IsKeyDown(VirtualKeyTab),
            IsKeyDown(VirtualKeyEnter),
            IsKeyDown(VirtualKeyF4));

    internal static bool IsDisqualifyingAltChord(
        bool controlDown,
        bool shiftDown,
        bool tabDown,
        bool enterDown,
        bool f4Down) =>
        controlDown
        || shiftDown
        || tabDown
        || enterDown
        || f4Down;

    internal static bool SetClickThrough(
        System.Windows.Window window,
        bool enabled)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }
        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate;
        style = enabled
            ? style | WsExTransparent
            : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        RefreshFrame(handle);
        long confirmedStyle =
            GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        long required = WsExToolWindow | WsExNoActivate;
        bool hasRequired = (confirmedStyle & required) == required;
        bool hasTransparent =
            (confirmedStyle & WsExTransparent) != 0;
        return hasRequired && hasTransparent == enabled;
    }

    internal static void SetOwner(
        System.Windows.Window window,
        IntPtr owner)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || owner == IntPtr.Zero)
        {
            return;
        }
        SetWindowLongPtr(handle, GwlHwndParent, owner);
        RefreshFrame(handle);
    }

    internal static void BeginMove(System.Windows.Window window) =>
        BeginNonClientAction(window, HtCaption);

    internal static void BeginBottomRightResize(
        System.Windows.Window window) =>
        BeginNonClientAction(window, HtBottomRight);

    private static void BeginNonClientAction(
        System.Windows.Window window,
        int hitTest)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        ReleaseCapture();
        SendMessage(
            handle,
            WmNcLeftButtonDown,
            new IntPtr(hitTest),
            IntPtr.Zero);
    }

    private static bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static void RefreshFrame(IntPtr handle)
    {
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize
            | SwpNoMove
            | SwpNoZOrder
            | SwpNoActivate
            | SwpFrameChanged);
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

    internal bool IsForeground
    {
        get
        {
            IntPtr foreground = NativeWindow.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }
            NativeWindow.GetWindowThreadProcessId(
                foreground,
                out uint owner);
            return owner == processId;
        }
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

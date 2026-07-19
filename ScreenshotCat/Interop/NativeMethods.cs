using System.Runtime.InteropServices;
using System.Text;

namespace ScreenshotCat.Interop;

internal static partial class NativeMethods
{
    internal const int MonitorDefaultToNearest = 0x00000002;
    internal const int SwHide = 0;
    internal const int SwShow = 5;
    internal const int SwShowNoActivate = 4;
    internal const int WhHotkey = 0x0312;
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmNcHitTest = 0x0084;
    internal const int HtTransparent = -1;
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const int GwlHwndParent = -8;
    internal const int DwmwaBorderColor = 34;
    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwaNcRenderingPolicy = 2;
    internal const int DwmwaUseHostBackdropBrush = 17;
    internal const int GaRoot = 2;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsCaption = 0x00C00000L;
    internal const long WsThickFrame = 0x00040000L;
    internal const long WsBorder = 0x00800000L;
    internal const long WsPopup = unchecked((long)0x80000000);
    internal const long WsExDlgModalFrame = 0x00000001L;
    internal const long WsExWindowEdge = 0x00000100L;
    internal const long WsExClientEdge = 0x00000200L;
    internal const long WsExStaticEdge = 0x00020000L;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const int VkControl = 0x11;
    internal const int VkMenu = 0x12;
    internal const int VkQ = 0x51;
    internal const int VkScroll = 0x91;
    internal const int VkN = 0x4E;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint DwmColorNone = 0xFFFFFFFE;
    internal const uint PwRenderFullContent = 0x00000002;
    internal const int WcaAccentPolicy = 19;
    internal const int AccentEnableAcrylicBlurBehind = 4;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint WineventOutOfContext = 0x0000;
    internal const uint WineventSkipOwnProcess = 0x0002;
    internal const long WsExNoActivate = 0x08000000L;
    internal const int ObjidWindow = 0;
    internal const int ChildidSelf = 0;

    internal delegate nint SubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint idSubclass, nuint refData);
    internal delegate nint LowLevelKeyboardProc(int nCode, nuint wParam, nint lParam);
    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    internal delegate void WinEventProc(
        nint winEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [LibraryImport("comctl32.dll")]
    internal static partial nint DefSubclassProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    internal static partial nint WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventProc winEventProc,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint winEventHook);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    internal static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    internal static partial nint GetAncestor(nint hwnd, uint gaFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BringWindowToTop(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    internal static partial nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(nint hwnd, int attribute, in uint value, int valueSize);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmExtendFrameIntoClientArea(nint hwnd, in MARGINS margins);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint handle);

    [LibraryImport("user32.dll")]
    internal static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowCompositionAttribute(nint hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    internal static void DisableDwmBorder(nint hwnd)
    {
        var doNotRound = 1u;
        // Keep DWM non-client rendering enabled so Windows 11 honors
        // DWMWA_BORDER_COLOR. Disabling it can fall back to a classic white
        // frame even when the Win32 caption/border styles were removed.
        var enableNonClientRendering = 2u;
        var borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, in enableNonClientRendering, sizeof(uint));
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, in doNotRound, sizeof(uint));
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, in borderColor, sizeof(uint));
    }

    internal static int DipToPhysicalPixels(nint hwnd, int dip)
    {
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return Math.Max(1, (int)Math.Round(dip * dpi / 96d));
    }

    internal static void BlendDwmBorder(nint hwnd, uint colorRef)
    {
        var borderColor = colorRef;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, in borderColor, sizeof(uint));
    }

    internal static void EnableHostBackdrop(nint hwnd)
    {
        var enabled = 1u;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseHostBackdropBrush, in enabled, sizeof(uint));
    }

    internal static void ExtendFrameThroughClientArea(nint hwnd)
    {
        var margins = new MARGINS
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };
        _ = DwmExtendFrameIntoClientArea(hwnd, in margins);
    }

    internal static void ApplyBorderlessRegion(nint hwnd, int width, int height, int inset = 0)
    {
        if (width < 1 || height < 1 || inset < 0 || inset * 2 >= width || inset * 2 >= height)
        {
            return;
        }

        var region = CreateRectRgn(inset, inset, width - inset, height - inset);
        if (region == 0)
        {
            return;
        }

        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            _ = DeleteObject(region);
        }
    }

    internal static void ApplyAcrylicBlur(nint hwnd, uint gradientColor)
    {
        var policy = new ACCENTPOLICY
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = 0,
            GradientColor = unchecked((int)gradientColor),
            AnimationId = 0
        };
        var policyPointer = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENTPOLICY>());
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = Marshal.SizeOf<ACCENTPOLICY>()
            };
            _ = SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    internal static void ConfigureBorderlessToolWindow(nint hwnd, nint ownerHwnd = 0)
    {
        ConfigureBorderlessTopmostWindow(hwnd, ownerHwnd, noActivate: true);
    }

    internal static void ConfigureBorderlessOverlayWindow(nint hwnd)
    {
        ConfigureBorderlessTopmostWindow(hwnd, ownerHwnd: 0, noActivate: false);
    }

    private static void ConfigureBorderlessTopmostWindow(nint hwnd, nint ownerHwnd, bool noActivate)
    {
        if (hwnd == 0)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var borderlessStyle = (style & ~(WsCaption | WsThickFrame | WsBorder)) | WsPopup;
        _ = SetWindowLongPtr(hwnd, GwlStyle, new nint(borderlessStyle));

        _ = SetWindowLongPtr(hwnd, GwlHwndParent, ownerHwnd);
        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        var borderlessExStyle = exStyle
            & ~(WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExNoActivate);
        var requestedExStyle = borderlessExStyle | WsExToolWindow;
        if (noActivate)
        {
            requestedExStyle |= WsExNoActivate;
        }

        _ = SetWindowLongPtr(
            hwnd,
            GwlExStyle,
            new nint(requestedExStyle));
        _ = SetWindowPos(
            hwnd,
            new nint(-1),
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    internal static void SetMouseClickThrough(nint hwnd, bool enabled)
    {
        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        var updated = enabled
            ? exStyle | WsExTransparent
            : exStyle & ~WsExTransparent;
        if (updated == exStyle)
        {
            return;
        }

        _ = SetWindowLongPtr(hwnd, GwlExStyle, new nint(updated));
        _ = SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ACCENTPOLICY
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}

using System.Runtime.InteropServices;

namespace WinPopCast.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nuint wParam;
    public nint lParam;
    public uint time;
    public int ptX, ptY;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;
}

internal delegate bool MonitorEnumProc(nint hMonitor, nint hdc, nint lprcMonitor, nint dwData);

internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

internal delegate void WinEventProc(
    nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

internal static partial class User32
{
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    public const uint PM_REMOVE = 0x0001;
    public const uint WM_QUIT = 0x0012;
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint MWMO_INPUTAVAILABLE = 0x0004;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint GW_OWNER = 4;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hWnd, char[] lpClassName, int nMaxCount);

    // GetWindowText と違い WM_GETTEXT を送らないので、相手プロセスがハングしていても止まらない
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int InternalGetWindowText(nint hWnd, char[] pString, int cchMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessageW(in MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessageW(uint idThread, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjectsEx(
        uint nCount, nint pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);
}

internal static class Shcore
{
    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}

internal static class Kernel32
{
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}

internal static class Dwmapi
{
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
}

/// <summary>SetWinEventHook で購読するイベント定数</summary>
public static class WinEvents
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MENUSTART = 0x0004;
    public const uint EVENT_SYSTEM_MENUEND = 0x0005;
    public const uint EVENT_SYSTEM_MENUPOPUPSTART = 0x0006;
    public const uint EVENT_SYSTEM_MENUPOPUPEND = 0x0007;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_REORDER = 0x8004;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    public static string Name(uint e) => e switch
    {
        EVENT_SYSTEM_FOREGROUND => "FOREGROUND",
        EVENT_SYSTEM_MENUSTART => "MENUSTART",
        EVENT_SYSTEM_MENUEND => "MENUEND",
        EVENT_SYSTEM_MENUPOPUPSTART => "MENUPOPUPSTART",
        EVENT_SYSTEM_MENUPOPUPEND => "MENUPOPUPEND",
        EVENT_SYSTEM_MOVESIZESTART => "MOVESIZESTART",
        EVENT_SYSTEM_MOVESIZEEND => "MOVESIZEEND",
        EVENT_SYSTEM_MINIMIZESTART => "MINIMIZESTART",
        EVENT_SYSTEM_MINIMIZEEND => "MINIMIZEEND",
        EVENT_OBJECT_CREATE => "CREATE",
        EVENT_OBJECT_DESTROY => "DESTROY",
        EVENT_OBJECT_SHOW => "SHOW",
        EVENT_OBJECT_HIDE => "HIDE",
        EVENT_OBJECT_REORDER => "REORDER",
        EVENT_OBJECT_LOCATIONCHANGE => "LOCATIONCHANGE",
        EVENT_OBJECT_NAMECHANGE => "NAMECHANGE",
        EVENT_OBJECT_CLOAKED => "CLOAKED",
        EVENT_OBJECT_UNCLOAKED => "UNCLOAKED",
        _ => $"0x{e:X4}",
    };
}

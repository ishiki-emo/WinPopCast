using System.Runtime.InteropServices;
using AppCapture.Core.Interop;

namespace AppCapture.Core.Tracking;

public static class Monitors
{
    /// <summary>モニターの矩形（物理ピクセル。Per-Monitor DPI Aware V2 で動いている前提）</summary>
    public static RectI? GetBounds(nint hmonitor)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (hmonitor == 0 || !User32.GetMonitorInfoW(hmonitor, ref mi)) return null;
        return new RectI(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
    }
}

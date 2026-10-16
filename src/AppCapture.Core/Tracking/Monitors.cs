using System.Runtime.InteropServices;
using AppCapture.Core.Interop;

namespace AppCapture.Core.Tracking;

/// <summary>モニター 1 台の情報。座標はすべて物理ピクセル（Per-Monitor DPI Aware V2 で動いている前提）</summary>
public sealed record MonitorInfo(nint Handle, string DeviceName, RectI Bounds, RectI WorkArea, uint Dpi, bool IsPrimary)
{
    public double Scale => Dpi / 96.0;
}

public static class Monitors
{
    private const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>接続中のモニター（左上から順）</summary>
    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var handles = new List<nint>();
        User32.EnumDisplayMonitors(0, 0, (h, _, _, _) => { handles.Add(h); return true; }, 0);
        return handles.Select(Get).OfType<MonitorInfo>()
            .OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    public static MonitorInfo? Get(nint hmonitor)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (hmonitor == 0 || !User32.GetMonitorInfoW(hmonitor, ref mi)) return null;
        uint dpi = Shcore.GetDpiForMonitor(hmonitor, Shcore.MDT_EFFECTIVE_DPI, out var dx, out _) == 0 ? dx : 96;
        return new MonitorInfo(
            hmonitor,
            mi.szDevice,
            new RectI(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom),
            new RectI(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom),
            dpi,
            (mi.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    public static RectI? GetBounds(nint hmonitor) => Get(hmonitor)?.Bounds;

    /// <summary>全モニターを囲む矩形（仮想デスクトップ）</summary>
    public static RectI VirtualDesktop(IReadOnlyList<MonitorInfo> monitors) =>
        monitors.Count == 0
            ? default
            : new RectI(monitors.Min(m => m.Bounds.Left), monitors.Min(m => m.Bounds.Top),
                monitors.Max(m => m.Bounds.Right), monitors.Max(m => m.Bounds.Bottom));
}

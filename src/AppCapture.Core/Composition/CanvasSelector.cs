using System.Diagnostics;
using AppCapture.Core.Tracking;

namespace AppCapture.Core.Composition;

public enum CanvasMode
{
    /// <summary>メインウィンドウがあるモニター（移動に追従する）</summary>
    Auto,
    /// <summary>指定したモニター</summary>
    Monitor,
    /// <summary>全モニターを囲む矩形</summary>
    VirtualDesktop,
    /// <summary>メインウィンドウの矩形</summary>
    MainWindow,
}

/// <summary>
/// キャンバスの指定。文字列表現は "auto" / "virtual" / "window" / モニター指定
/// （"primary"、1 始まりの番号、"\\.\DISPLAY2" のようなデバイス名）。
/// </summary>
public sealed record CanvasSpec(CanvasMode Mode, string? Monitor = null)
{
    public static CanvasSpec Default { get; } = new(CanvasMode.Auto);

    public static CanvasSpec Parse(string s) => s.ToLowerInvariant() switch
    {
        "auto" => new(CanvasMode.Auto),
        "virtual" => new(CanvasMode.VirtualDesktop),
        "window" => new(CanvasMode.MainWindow),
        _ => new(CanvasMode.Monitor, s),
    };

    public override string ToString() => Mode switch
    {
        CanvasMode.Auto => "auto",
        CanvasMode.VirtualDesktop => "virtual",
        CanvasMode.MainWindow => "window",
        _ => Monitor ?? "primary",
    };
}

/// <summary>キャンバスの指定とウィンドウの状態から、毎フレームのキャンバス矩形を決める</summary>
public sealed class CanvasSelector
{
    private static readonly long RefreshInterval = Stopwatch.Frequency; // モニター構成は 1 秒ごとに取り直す

    private IReadOnlyList<MonitorInfo> _monitors = [];
    private long _nextRefresh;
    private RectI _last;

    public CanvasSelector(CanvasSpec spec) => Spec = spec;

    public CanvasSpec Spec { get; set; }

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    /// <summary>指定したモニターが見つからないとき（auto と同じ動きにフォールバックする）</summary>
    public bool MonitorMissing { get; private set; }

    public RectI Resolve(WindowSnapshot snapshot)
    {
        long now = Stopwatch.GetTimestamp();
        if (now >= _nextRefresh)
        {
            _monitors = Tracking.Monitors.GetAll();
            _nextRefresh = now + RefreshInterval;
        }

        var main = snapshot.FindMainWindow();
        MonitorMissing = false;
        RectI? rect = Spec.Mode switch
        {
            CanvasMode.VirtualDesktop => Tracking.Monitors.VirtualDesktop(_monitors),
            CanvasMode.MainWindow => main?.Bounds,
            CanvasMode.Monitor => FindMonitor(Spec.Monitor)?.Bounds ?? Fallback(),
            _ => MonitorOf(main),
        };

        // メインウィンドウが一時的に見つからないときは直前のキャンバスを保つ（出力解像度を揺らさない）
        if (rect is { IsEmpty: false } r) _last = r;
        else if (_last.IsEmpty) _last = _monitors.FirstOrDefault(m => m.IsPrimary)?.Bounds ?? default;
        return _last;

        RectI? Fallback()
        {
            MonitorMissing = true;
            return MonitorOf(main);
        }
    }

    private RectI? MonitorOf(TrackedWindow? w) =>
        w is null ? null : (_monitors.FirstOrDefault(m => m.Handle == w.Monitor)?.Bounds ?? Tracking.Monitors.GetBounds(w.Monitor));

    public MonitorInfo? FindMonitor(string? spec)
    {
        if (string.IsNullOrEmpty(spec) || spec.Equals("primary", StringComparison.OrdinalIgnoreCase))
            return _monitors.FirstOrDefault(m => m.IsPrimary);
        if (int.TryParse(spec, out int index))
            return index >= 1 && index <= _monitors.Count ? _monitors[index - 1] : null;
        return _monitors.FirstOrDefault(m => m.DeviceName.Equals(spec, StringComparison.OrdinalIgnoreCase));
    }
}

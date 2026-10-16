using WinPopCast.Core.Graphics;
using WinPopCast.Core.Tracking;

namespace WinPopCast.Core.Capture;

/// <summary>
/// スナップショットに合わせて、ウィンドウごとの WGC セッションを生成・破棄する。
/// </summary>
public sealed class CaptureManager : IDisposable
{
    private readonly D3DContext _d3d;
    private readonly Dictionary<nint, WindowCapture> _captures = [];
    private readonly HashSet<nint> _failed = []; // キャプチャできなかったウィンドウ（毎回リトライしない）
    private readonly object _lock = new();
    private bool _disposed;

    public CaptureManager(D3DContext d3d) => _d3d = d3d;

    public event Action<WindowCapture, TrackedWindow>? CaptureStarted;
    public event Action<TrackedWindow, Exception>? CaptureFailed;
    public event Action<WindowCapture>? FirstFrameArrived;

    /// <summary>キャプチャ中の例外（フレームプールのスレッドで発火）</summary>
    public event Action<WindowCapture, Exception>? Error;

    /// <summary>トラッカーのスナップショットに同期させる（どのスレッドから呼んでもよい）</summary>
    public void Sync(WindowSnapshot snapshot)
    {
        var wanted = snapshot.Windows.ToDictionary(w => w.Handle);
        List<WindowCapture> toDispose = [];

        lock (_lock)
        {
            if (_disposed) return;

            foreach (var (hwnd, cap) in _captures)
                if (!wanted.ContainsKey(hwnd)) toDispose.Add(cap);
            foreach (var cap in toDispose) _captures.Remove(cap.Handle);
            _failed.IntersectWith(wanted.Keys);

            foreach (var w in snapshot.Windows)
            {
                if (_captures.ContainsKey(w.Handle) || _failed.Contains(w.Handle)) continue;
                try
                {
                    var cap = WindowCapture.Start(_d3d, w.Handle);
                    cap.FirstFrameArrived += c => FirstFrameArrived?.Invoke(c);
                    cap.Error += (c, ex) => Error?.Invoke(c, ex);
                    cap.Closed += OnClosed;
                    _captures[w.Handle] = cap;
                    CaptureStarted?.Invoke(cap, w);
                }
                catch (Exception ex)
                {
                    _failed.Add(w.Handle);
                    CaptureFailed?.Invoke(w, ex);
                }
            }
        }

        foreach (var cap in toDispose) cap.Dispose();
    }

    /// <summary>ウィンドウに対応するキャプチャ（なければ null）</summary>
    public WindowCapture? Get(nint hwnd)
    {
        lock (_lock) return _captures.GetValueOrDefault(hwnd);
    }

    public int Count
    {
        get { lock (_lock) return _captures.Count; }
    }

    private void OnClosed(WindowCapture cap)
    {
        lock (_lock)
        {
            if (!_captures.Remove(cap.Handle)) return;
        }
        cap.Dispose();
    }

    public void Dispose()
    {
        List<WindowCapture> all;
        lock (_lock)
        {
            _disposed = true;
            all = [.. _captures.Values];
            _captures.Clear();
        }
        foreach (var cap in all) cap.Dispose();
    }
}

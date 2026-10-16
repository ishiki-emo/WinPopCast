using WinPopCast.Core.Graphics;
using WinPopCast.Core.Tracking;

namespace WinPopCast.Core.Capture;

/// <summary>
/// スナップショットに合わせて、ウィンドウごとの WGC セッションを生成・破棄する。
///
/// WGC のセッションを閉じる処理（GraphicsCaptureSession.Close）は、まれに戻ってこないことがある。
/// それに追跡（WindowTracker）や合成が巻き込まれて止まらないよう、
/// 生成・破棄は専用のスレッドで行い、破棄はさらに別のタスクで行って一定時間で見切る。
/// </summary>
public sealed class CaptureManager : IDisposable
{
    /// <summary>セッションの破棄がこれより長くかかったら、待たずに先へ進む（警告を出す）</summary>
    public static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(2);

    private readonly D3DContext _d3d;
    private readonly Dictionary<nint, WindowCapture> _captures = [];
    private readonly HashSet<nint> _failed = []; // キャプチャできなかったウィンドウ（毎回リトライしない）
    private readonly object _lock = new();
    private readonly Thread _worker;
    private readonly AutoResetEvent _wake = new(false);
    private WindowSnapshot? _pending; // 最新のスナップショットだけを処理する（古いものは飛ばす）
    private volatile bool _disposed;
    private int _hungDisposals;

    public CaptureManager(D3DContext d3d)
    {
        _d3d = d3d;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "CaptureManager" };
        _worker.Start();
    }

    public event Action<WindowCapture, TrackedWindow>? CaptureStarted;
    public event Action<TrackedWindow, Exception>? CaptureFailed;
    public event Action<WindowCapture>? FirstFrameArrived;

    /// <summary>キャプチャ中の例外（フレームプールのスレッドなどで発火）</summary>
    public event Action<WindowCapture?, Exception>? Error;

    /// <summary>破棄が戻ってこなかったセッションの数（見切って先へ進んだもの）</summary>
    public int HungDisposals => Volatile.Read(ref _hungDisposals);

    /// <summary>
    /// トラッカーのスナップショットに同期させる。すぐに戻り、実際の生成・破棄は専用スレッドで行う
    /// （どのスレッドから呼んでもよい）
    /// </summary>
    public void Sync(WindowSnapshot snapshot)
    {
        if (_disposed) return;
        Volatile.Write(ref _pending, snapshot);
        _wake.Set();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _wake.WaitOne();
            if (_disposed) return;
            var snapshot = Interlocked.Exchange(ref _pending, null);
            if (snapshot is null) continue;
            try
            {
                Apply(snapshot);
            }
            catch (Exception ex)
            {
                RaiseError(null, ex);
            }
        }
    }

    private void Apply(WindowSnapshot snapshot)
    {
        var wanted = snapshot.Windows.ToDictionary(w => w.Handle);
        List<WindowCapture> toDispose = [];
        List<TrackedWindow> toStart = [];

        lock (_lock)
        {
            foreach (var (hwnd, cap) in _captures)
                if (!wanted.ContainsKey(hwnd)) toDispose.Add(cap);
            foreach (var cap in toDispose) _captures.Remove(cap.Handle);
            _failed.IntersectWith(wanted.Keys);
            foreach (var w in snapshot.Windows)
                if (!_captures.ContainsKey(w.Handle) && !_failed.Contains(w.Handle)) toStart.Add(w);
        }

        // 消えた窓の片付けは別のタスクで（戻ってこなくても、新しい窓のキャプチャを遅らせない）
        foreach (var cap in toDispose) DisposeDetached(cap);

        // 生成はロックの外で（合成側の Get を待たせない）
        foreach (var w in toStart)
        {
            if (_disposed) return;
            WindowCapture cap;
            try
            {
                cap = WindowCapture.Start(_d3d, w.Handle);
            }
            catch (Exception ex)
            {
                lock (_lock) _failed.Add(w.Handle);
                CaptureFailed?.Invoke(w, ex);
                continue;
            }
            cap.FirstFrameArrived += c => FirstFrameArrived?.Invoke(c);
            cap.Error += (c, ex) => RaiseError(c, ex);
            cap.Closed += OnClosed;
            lock (_lock) _captures[w.Handle] = cap;
            CaptureStarted?.Invoke(cap, w);
        }

        // 生成している間に、さらに新しいスナップショットが来ていればすぐ続ける
        if (Volatile.Read(ref _pending) is not null) _wake.Set();
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
        // WGC のコールバックの中なので、ここでは閉じない（コールバック内で Close すると止まることがある）
        lock (_lock)
        {
            if (!_captures.Remove(cap.Handle)) return;
        }
        DisposeDetached(cap);
    }

    /// <summary>セッションを別のタスクで閉じ、一定時間で戻らなければ見切って警告する</summary>
    private void DisposeDetached(WindowCapture cap)
    {
        var task = Task.Run(cap.Dispose);
        _ = task.WaitAsync(DisposeTimeout).ContinueWith(t =>
        {
            if (t.Exception?.InnerException is TimeoutException)
            {
                Interlocked.Increment(ref _hungDisposals);
                RaiseError(cap, new TimeoutException(
                    $"キャプチャの終了が {DisposeTimeout.TotalSeconds:F0} 秒以内に戻りませんでした（0x{(long)cap.Handle:X}）。待たずに続行します"));
            }
            else if (t.Exception?.InnerException is { } ex)
            {
                RaiseError(cap, ex);
            }
        }, TaskScheduler.Default);
    }

    private void RaiseError(WindowCapture? cap, Exception ex)
    {
        try { Error?.Invoke(cap, ex); } catch { }
    }

    public void Dispose()
    {
        List<WindowCapture> all;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            all = [.. _captures.Values];
            _captures.Clear();
        }
        _wake.Set();
        _worker.Join(DisposeTimeout);

        // 終了時もセッションの破棄で止まらないよう、全体で待つ時間を区切る
        var tasks = all.Select(c => Task.Run(c.Dispose)).ToArray();
        try { Task.WaitAll(tasks, DisposeTimeout); } catch { }
        if (tasks.Any(t => !t.IsCompleted)) Interlocked.Add(ref _hungDisposals, tasks.Count(t => !t.IsCompleted));
    }
}

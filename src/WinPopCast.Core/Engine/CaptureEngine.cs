using System.Diagnostics;
using WinPopCast.Core.Capture;
using WinPopCast.Core.Composition;
using WinPopCast.Core.Graphics;
using WinPopCast.Core.Timing;
using WinPopCast.Core.Tracking;
using Vortice.Direct3D11;

namespace WinPopCast.Core.Engine;

/// <summary>合成結果の送り先（Spout など）。D3DContext.Lock を取った状態で呼ばれる</summary>
public interface IFrameSink
{
    void Send(ID3D11Texture2D texture);
}

public sealed class EngineOptions
{
    public required TrackerOptions Tracker { get; init; }
    public int Fps { get; init; } = 60;
    public CanvasSpec Canvas { get; init; } = CanvasSpec.Default;

    /// <summary>
    /// 映像に変化がないときに、同じ内容を送り直す間隔。変化があるときは毎フレーム送る。
    /// 受信側から送信が止まったと見なされないようにするためのもの
    /// </summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>エンジンの状態（約 1 秒ごとに更新）</summary>
public sealed record EngineStatus(
    IReadOnlyCollection<int> TargetProcessIds,
    int WindowCount,
    int CaptureCount,
    RectI Canvas,
    bool CanvasMonitorMissing,
    /// <summary>実際に合成・送信した回数（毎秒）。変化がないときは KeepAliveInterval ごとに下がる</summary>
    double SendFps,
    double RenderMs,
    /// <summary>上限のフレームレート</summary>
    int TargetFps);

/// <summary>プレビュー用の縮小フレーム（BGRA8、乗算済みアルファ）</summary>
public sealed record PreviewFrame(byte[] Pixels, int Width, int Height);

/// <summary>
/// ウィンドウ追跡 → キャプチャ → 合成 → 出力 のパイプライン。専用スレッドで一定間隔に合成して sink に渡す。
/// </summary>
public sealed class CaptureEngine : IDisposable
{
    private readonly D3DContext _d3d;
    private readonly IFrameSink _sink;
    private readonly int _fps;
    private readonly long _keepAliveTicks;
    private readonly Compositor _compositor;
    private readonly CanvasSelector _canvasSelector;
    private readonly Thread _thread;
    private volatile bool _stopping;

    public CaptureEngine(D3DContext d3d, EngineOptions options, IFrameSink sink)
    {
        _d3d = d3d;
        _sink = sink;
        _fps = options.Fps;
        _keepAliveTicks = (long)(options.KeepAliveInterval.TotalSeconds * Stopwatch.Frequency);
        _compositor = new Compositor(d3d);
        _canvasSelector = new CanvasSelector(options.Canvas);
        Captures = new CaptureManager(d3d);
        Tracker = new WindowTracker(options.Tracker);
        Tracker.SnapshotChanged += (now, prev, trigger) =>
        {
            BeforeSync?.Invoke(now, prev, trigger);
            Captures.Sync(now);
        };
        Tracker.Error += RaiseError;
        Captures.Error += (_, ex) => RaiseError(ex);
        _thread = new Thread(RenderLoop) { IsBackground = true, Name = "CaptureEngine", Priority = ThreadPriority.AboveNormal };
    }

    public WindowTracker Tracker { get; }
    public CaptureManager Captures { get; }

    /// <summary>キャンバスの指定（実行中に変更してよい）</summary>
    public CanvasSpec Canvas
    {
        get => _canvasSelector.Spec;
        set => _canvasSelector.Spec = value;
    }

    /// <summary>プレビューを作るか、とその最大サイズ</summary>
    public bool PreviewEnabled { get; set; }
    public int PreviewMaxWidth { get; set; } = 640;
    public int PreviewMaxHeight { get; set; } = 400;
    public double PreviewFps { get; set; } = 10;

    public EngineStatus? Status { get; private set; }

    /// <summary>スナップショットが変わり、キャプチャを同期する直前（トラッカーのスレッド）</summary>
    public event Action<WindowSnapshot, WindowSnapshot, long>? BeforeSync;

    /// <summary>キャンバスが変わったとき（エンジンのスレッド）</summary>
    public event Action<RectI>? CanvasChanged;

    /// <summary>プレビューの縮小フレーム（エンジンのスレッド）</summary>
    public event Action<PreviewFrame>? PreviewReady;

    /// <summary>状態が更新された（エンジンのスレッド）</summary>
    public event Action<EngineStatus>? StatusUpdated;

    /// <summary>
    /// 追跡・キャプチャ・合成で例外が出たとき（各スレッドで発火）。エンジンは続行する
    /// </summary>
    public event Action<Exception>? Error;

    /// <summary>
    /// GPU のデバイスが失われた（ドライバーの更新・リセットなど）。合成ループは止まるので、
    /// 受け手は D3DContext ごとエンジンを作り直すこと（エンジンのスレッドで 1 回だけ発火）
    /// </summary>
    public event Action<string>? DeviceLost;

    /// <summary>デバイスが失われて止まっているか</summary>
    public bool IsDeviceLost { get; private set; }

    private void RaiseError(Exception ex)
    {
        try { Error?.Invoke(ex); } catch { }
    }

    private volatile bool _simulateDeviceLost;

    /// <summary>復帰処理の確認用：次の確認時にデバイスが失われたものとして扱う</summary>
    public void SimulateDeviceLost() => _simulateDeviceLost = true;

    /// <summary>デバイスが失われていれば、理由を返す</summary>
    private string? CheckDeviceRemoved()
    {
        if (_simulateDeviceLost) return "simulated";
        var reason = _d3d.Device.DeviceRemovedReason;
        return reason.Failure ? $"0x{reason.Code:X8} {reason.Description}" : null;
    }

    private void HandleDeviceLost(string reason)
    {
        if (IsDeviceLost) return;
        IsDeviceLost = true;
        _stopping = true;
        try { DeviceLost?.Invoke(reason); } catch { }
    }

    public void Start()
    {
        Tracker.Start();
        _thread.Start();
    }

    private void RenderLoop()
    {
        using var pacer = new FramePacer(_fps);
        var layers = new List<Layer>();
        var caps = new List<(TrackedWindow W, WindowCapture C)>();
        RectI canvas = default;
        long sent = 0, lastSent = 0, lastStats = Stopwatch.GetTimestamp(), nextPreview = 0, lastSend = 0;
        int lastSignature = 0;
        bool hasSent = false;
        double renderMsTotal = 0;

        while (!_stopping)
        {
            pacer.Wait();
            try
            {
                var snapshot = Tracker.Current;
                var next = _canvasSelector.Resolve(snapshot);
                if (next != canvas)
                {
                    canvas = next;
                    CanvasChanged?.Invoke(canvas);
                }
                if (canvas.IsEmpty) continue;

                // 下（奥）から上（手前）へ。フレームが未着のウィンドウは描かない（映らない側に倒す）
                caps.Clear();
                for (int i = snapshot.Windows.Count - 1; i >= 0; i--)
                    if (Captures.Get(snapshot.Windows[i].Handle) is { } c) caps.Add((snapshot.Windows[i], c));

                long r0 = Stopwatch.GetTimestamp();
                PreviewFrame? preview = null;
                lock (_d3d.Lock)
                {
                    // 映像を決めるもの（キャンバス、各窓の位置・サイズ・テクスチャ・フレーム番号）が変わったときだけ描く。
                    // 新しいフレームが届けばフレーム番号が変わるので、届いた最新のフレームは必ず次の周期で送られる
                    var sig = new HashCode();
                    sig.Add(canvas);
                    layers.Clear();
                    foreach (var (w, c) in caps)
                    {
                        if (c.Texture is not { } tex) continue;
                        layers.Add(new Layer(tex, w.Bounds.Left, w.Bounds.Top, c.ContentSize.Width, c.ContentSize.Height));
                        sig.Add(c.Handle); sig.Add(tex.NativePointer); sig.Add(c.FrameCount);
                        sig.Add(w.Bounds.Left); sig.Add(w.Bounds.Top); sig.Add(c.ContentSize.Width); sig.Add(c.ContentSize.Height);
                    }
                    int signature = sig.ToHashCode();
                    bool changed = !hasSent || signature != lastSignature;
                    if (changed || r0 - lastSend >= _keepAliveTicks)
                    {
                        var output = _compositor.Render(canvas, layers);
                        _sink.Send(output);
                        sent++;
                        hasSent = true;
                        lastSignature = signature;
                        lastSend = r0;

                        if (PreviewEnabled && PreviewReady is not null && r0 >= nextPreview)
                        {
                            nextPreview = r0 + (long)(Stopwatch.Frequency / PreviewFps);
                            double scale = Math.Min(1.0, Math.Min((double)PreviewMaxWidth / canvas.Width, (double)PreviewMaxHeight / canvas.Height));
                            var small = _compositor.RenderScaled(output, Math.Max(1, (int)(canvas.Width * scale)), Math.Max(1, (int)(canvas.Height * scale)));
                            var (px, pw, ph) = TextureDump.Read(_d3d, small);
                            preview = new PreviewFrame(px, pw, ph);
                        }
                        renderMsTotal += (Stopwatch.GetTimestamp() - r0) * 1000.0 / Stopwatch.Frequency;
                    }
                }
                if (preview is not null) PreviewReady?.Invoke(preview);

                long now = Stopwatch.GetTimestamp();
                if (now - lastStats >= Stopwatch.Frequency)
                {
                    if (CheckDeviceRemoved() is { } removed)
                    {
                        HandleDeviceLost(removed);
                        break;
                    }
                    double sec = (now - lastStats) / (double)Stopwatch.Frequency;
                    long n = sent - lastSent;
                    Status = new EngineStatus(snapshot.TargetProcessIds, snapshot.Windows.Count, Captures.Count, canvas,
                        _canvasSelector.MonitorMissing, n / sec, n > 0 ? renderMsTotal / n : 0, _fps);
                    lastSent = sent; lastStats = now; renderMsTotal = 0;
                    StatusUpdated?.Invoke(Status);
                }
            }
            catch (Exception ex)
            {
                if (CheckDeviceRemoved() is { } removed)
                {
                    HandleDeviceLost(removed);
                    break;
                }
                RaiseError(ex);
            }
        }
    }

    public void Dispose()
    {
        _stopping = true;
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
        Tracker.Dispose();
        Captures.Dispose();
        lock (_d3d.Lock) _compositor.Dispose();
    }
}

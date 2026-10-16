using WinPopCast.Core.Capture;
using WinPopCast.Core.Composition;
using WinPopCast.Core.Engine;
using WinPopCast.Core.Graphics;
using WinPopCast.Core.Tracking;
using WinPopCast.Spout;

namespace WinPopCast.App;

/// <summary>設定に従ってエンジンと Spout 送信者を作り直す。UI スレッドから呼ぶ</summary>
public sealed class AppController : IDisposable
{
    private static readonly TimeSpan RecoveryRetryDelay = TimeSpan.FromSeconds(2);

    private readonly SynchronizationContext _ui;
    private D3DContext? _d3d;
    private SpoutSender? _sender;
    private CaptureEngine? _engine;
    private AppSettings? _applied;
    private bool _previewVisible;
    private bool _disposed;

    public AppController()
    {
        // UI スレッドで作られる前提。まだ WinForms のコンテキストが入っていなければ、ここで作る
        _ui = SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();
        _d3d = new D3DContext();
        _ = WindowCapture.RequestBorderlessAccessAsync();
    }

    public AppSettings? Settings => _applied;

    /// <summary>実行中のエンジンの状態（停止中は null）</summary>
    public EngineStatus? Status => _engine?.Status;

    public bool IsRunning => _engine is not null;

    /// <summary>送信できない状態のエラー（Spout 送信者の作成失敗、GPU からの復帰失敗など）</summary>
    public string? LastError { get; private set; }

    /// <summary>送信は続いている、直近の一時的なエラー（時刻つき）</summary>
    public string? LastWarning { get; private set; }

    /// <summary>GPU のデバイスロストから復帰した回数と、最後に復帰した時刻</summary>
    public int RecoveryCount { get; private set; }
    public DateTime? LastRecovery { get; private set; }

    /// <summary>プレビューの縮小フレーム（エンジンのスレッドで発火）</summary>
    public event Action<PreviewFrame>? PreviewReady;

    /// <summary>設定画面が表示されているときだけプレビューを作る</summary>
    public bool PreviewVisible
    {
        get => _previewVisible;
        set
        {
            _previewVisible = value;
            if (_engine is not null) _engine.PreviewEnabled = value && (_applied?.Preview ?? false);
        }
    }

    public void Apply(AppSettings settings)
    {
        var old = _applied;
        _applied = settings.Clone();
        LastError = null;

        bool shouldRun = settings.Enabled && !string.IsNullOrWhiteSpace(settings.ProcessName);
        bool senderChanged = old is null || old.SenderName != settings.SenderName;
        bool engineChanged = old is null || senderChanged || old.ProcessName != settings.ProcessName || old.Fps != settings.Fps ||
            old.HideFileDialogs != settings.HideFileDialogs ||
            !RulesEqual(old.Include, settings.Include) || !RulesEqual(old.Exclude, settings.Exclude);

        if (!shouldRun || engineChanged) StopEngine();
        if (!shouldRun || senderChanged)
        {
            _sender?.Dispose();
            _sender = null;
        }
        if (!shouldRun) return;

        try
        {
            _d3d ??= new D3DContext();
            _sender ??= new SpoutSender(_d3d, settings.SenderName);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return;
        }

        if (_engine is null)
        {
            var engine = new CaptureEngine(_d3d, new EngineOptions
            {
                Tracker = new TrackerOptions
                {
                    ProcessNames = [settings.ProcessName!],
                    Include = settings.Include.ToList(),
                    Exclude = settings.Exclude.ToList(),
                    HideFileDialogs = settings.HideFileDialogs,
                },
                Fps = settings.Fps,
                Canvas = CanvasSpec.Parse(settings.Canvas),
            }, _sender);
            engine.PreviewReady += f => PreviewReady?.Invoke(f);
            engine.Error += ex => LastWarning = $"{DateTime.Now:HH:mm:ss} {ex.Message}";
            engine.DeviceLost += reason => _ui.Post(_ => OnDeviceLost(engine, reason), null);
            _engine = engine;
            _engine.Start();
        }

        _engine.Canvas = CanvasSpec.Parse(settings.Canvas);
        _engine.PreviewEnabled = _previewVisible && settings.Preview;
    }

    /// <summary>復帰処理の確認用：デバイスロストが起きたものとして扱う</summary>
    public void SimulateDeviceLost() => _engine?.SimulateDeviceLost();

    /// <summary>
    /// GPU のデバイスが失われたら、D3D デバイス・Spout 送信者・エンジンをすべて作り直す。
    /// 作り直しに失敗したら（GPU がまだ戻っていないなど）、少し待ってからやり直す
    /// </summary>
    private void OnDeviceLost(CaptureEngine source, string reason)
    {
        if (_disposed || !ReferenceEquals(source, _engine)) return;
        LastWarning = $"{DateTime.Now:HH:mm:ss} GPU のデバイスが失われました（{reason}）";
        Recover();
    }

    private void Recover()
    {
        if (_disposed || _applied is null) return;
        var settings = _applied;

        StopEngine();
        _sender?.Dispose();
        _sender = null;
        _d3d?.Dispose();
        _d3d = null;
        _applied = null; // 全部作り直させる

        Apply(settings);
        if (_engine is null && settings.Enabled && !string.IsNullOrWhiteSpace(settings.ProcessName))
        {
            LastError = $"GPU からの復帰に失敗しました。再試行します（{LastError}）";
            _ = Task.Delay(RecoveryRetryDelay).ContinueWith(_ => _ui.Post(_ => Recover(), null));
            return;
        }
        RecoveryCount++;
        LastRecovery = DateTime.Now;
    }

    private void StopEngine()
    {
        _engine?.Dispose();
        _engine = null;
    }

    private static bool RulesEqual(List<WindowRule> a, List<WindowRule> b) =>
        a.Select(r => r.ToString()).SequenceEqual(b.Select(r => r.ToString()));

    public void Dispose()
    {
        _disposed = true;
        StopEngine();
        _sender?.Dispose();
        _d3d?.Dispose();
    }
}

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
    private readonly D3DContext _d3d = new();
    private SpoutSender? _sender;
    private CaptureEngine? _engine;
    private AppSettings? _applied;
    private bool _previewVisible;

    public AppController()
    {
        _ = WindowCapture.RequestBorderlessAccessAsync();
    }

    public AppSettings? Settings => _applied;

    /// <summary>実行中のエンジンの状態（停止中は null）</summary>
    public EngineStatus? Status => _engine?.Status;

    public bool IsRunning => _engine is not null;

    /// <summary>直近のエラー（Spout 送信者の作成失敗など）</summary>
    public string? LastError { get; private set; }

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
            _sender ??= new SpoutSender(_d3d, settings.SenderName);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return;
        }

        if (_engine is null)
        {
            _engine = new CaptureEngine(_d3d, new EngineOptions
            {
                Tracker = new TrackerOptions
                {
                    ProcessNames = [settings.ProcessName!],
                    Include = settings.Include.ToList(),
                    Exclude = settings.Exclude.ToList(),
                },
                Fps = settings.Fps,
                Canvas = CanvasSpec.Parse(settings.Canvas),
            }, _sender);
            _engine.PreviewReady += f => PreviewReady?.Invoke(f);
            _engine.Error += ex => LastError = ex.Message;
            _engine.Start();
        }

        _engine.Canvas = CanvasSpec.Parse(settings.Canvas);
        _engine.PreviewEnabled = _previewVisible && settings.Preview;
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
        StopEngine();
        _sender?.Dispose();
        _d3d.Dispose();
    }
}

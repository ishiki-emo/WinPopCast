using System.Drawing.Drawing2D;

namespace WinPopCast.App;

/// <summary>トレイアイコン（実行時に描く。配布前にちゃんとしたアイコンに差し替える）</summary>
internal static class TrayIcons
{
    public static Icon Active { get; } = Create(Color.FromArgb(0, 150, 255));
    public static Icon Idle { get; } = Create(Color.FromArgb(128, 128, 128));

    private static Icon Create(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var back = new SolidBrush(color);
            using var path = new GraphicsPath();
            path.AddArc(2, 2, 10, 10, 180, 90);
            path.AddArc(20, 2, 10, 10, 270, 90);
            path.AddArc(20, 20, 10, 10, 0, 90);
            path.AddArc(2, 20, 10, 10, 90, 90);
            path.CloseFigure();
            g.FillPath(back, path);
            // 重なった 2 枚の窓
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, 8, 9, 13, 10);
            using var front = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
            g.FillRectangle(back, 12, 13, 13, 10);
            g.FillRectangle(front, 13, 14, 11, 8);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

/// <summary>トレイに常駐するアプリ本体</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly string _settingsPath;
    private readonly AppController _controller;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _toggle;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly UpdateService _updates = new();
    private readonly ToolStripMenuItem _updateItem;
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 10_000 }; // 最初は起動 10 秒後、以降 6 時間ごと
    private AppSettings _settings;
    private SettingsForm? _form;

    public TrayApp(string settingsPath, bool showSettings, int testDeviceLostSeconds = 0, bool testUpdate = false)
    {
        _controller = new AppController();
        _settingsPath = settingsPath;
        _settings = AppSettings.Load(settingsPath);

        _toggle = new ToolStripMenuItem("送信を停止", null, (_, _) => SetEnabled(!_settings.Enabled));
        var menu = new ContextMenuStrip();
        menu.Items.Add("設定を開く", null, (_, _) => ShowSettings());
        menu.Items.Add(_toggle);
        menu.Items.Add(new ToolStripSeparator());
        var autoStart = new ToolStripMenuItem("Windows の起動時に起動する") { Checked = AutoStart.IsEnabled, CheckOnClick = true };
        autoStart.CheckedChanged += (_, _) =>
        {
            try { AutoStart.Set(autoStart.Checked); }
            catch (Exception ex) { MessageBox.Show($"設定できませんでした: {ex.Message}", "WinPopCast", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        menu.Items.Add(autoStart);
        _updateItem = new ToolStripMenuItem("更新して再起動", null, async (_, _) => await ApplyUpdateAsync()) { Visible = false };
        menu.Items.Add(_updateItem);
        if (_updates.IsInstalled) menu.Items.Add("更新を確認", null, async (_, _) => await CheckUpdateManuallyAsync());
        menu.Items.Add("バージョン情報とライセンス", null, (_, _) => AboutBox.Show());
        menu.Items.Add("終了", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.Idle,
            Text = "WinPopCast",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        _controller.Apply(_settings);
        _updates.UpdateAvailable += OnUpdateAvailable;
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = (int)TimeSpan.FromHours(6).TotalMilliseconds;
            await _updates.CheckAsync();
        };
        if (_updates.IsInstalled) _updateTimer.Start();
        _timer.Tick += (_, _) => UpdateTray();
        _timer.Start();
        UpdateTray();

        if (testDeviceLostSeconds > 0)
        {
            var t = new System.Windows.Forms.Timer { Interval = testDeviceLostSeconds * 1000 };
            t.Tick += (_, _) => { t.Dispose(); _controller.SimulateDeviceLost(); };
            t.Start();
        }

        if (testUpdate)
        {
            _updates.UpdateAvailable += async _ => await _updates.DownloadAndRestartAsync();
            _ = _updates.CheckAsync();
        }

        // 対象が未設定なら最初に設定画面を出す
        if (showSettings || string.IsNullOrWhiteSpace(_settings.ProcessName)) ShowSettings();
    }

    private void OnUpdateAvailable(string version)
    {
        _updateItem.Text = $"更新して再起動（{version}）";
        _updateItem.Visible = true;
        _tray.ShowBalloonTip(10_000, "WinPopCast", $"新しいバージョン {version} があります。配信していないときに、トレイのメニューから更新できます。", ToolTipIcon.Info);
    }

    private async Task CheckUpdateManuallyAsync()
    {
        await _updates.CheckAsync();
        if (_updates.AvailableVersion is null)
        {
            string msg = _updates.LastError is { } err ? $"更新を確認できませんでした。\n{err}" : $"最新の版です（{_updates.CurrentVersion}）。";
            MessageBox.Show(msg, "WinPopCast", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>ユーザーが選んだときだけ更新する。再起動で数秒間送信が止まるので確認を出す</summary>
    private async Task ApplyUpdateAsync()
    {
        var answer = MessageBox.Show(
            $"WinPopCast を {_updates.AvailableVersion} に更新して再起動します。\n再起動のあいだ、数秒間 OBS への送信が止まります。\n\n配信中の場合は、配信が終わってから更新してください。更新しますか？",
            "WinPopCast の更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        try
        {
            _updateItem.Enabled = false;
            _updateItem.Text = "更新をダウンロード中…";
            await _updates.DownloadAndRestartAsync(p => _updateItem.Text = $"更新をダウンロード中… {p}%");
        }
        catch (Exception ex)
        {
            _updateItem.Enabled = true;
            _updateItem.Text = $"更新して再起動（{_updates.AvailableVersion}）";
            MessageBox.Show($"更新できませんでした。\n{ex.Message}", "WinPopCast", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowSettings()
    {
        _form ??= new SettingsForm(_controller, _settings, Apply);
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    private void Apply(AppSettings settings)
    {
        _settings = settings;
        _settings.Save(_settingsPath);
        _controller.Apply(_settings);
        UpdateTray();
    }

    private void SetEnabled(bool enabled)
    {
        var s = _settings.Clone();
        s.Enabled = enabled;
        Apply(s);
    }

    private void UpdateTray()
    {
        _toggle.Text = _settings.Enabled ? "送信を停止" : "送信を再開";
        var s = _controller.Status;
        bool active = _controller.IsRunning && s is { TargetProcessIds.Count: > 0 };
        _tray.Icon = active ? TrayIcons.Active : TrayIcons.Idle;

        string text = !_settings.Enabled ? "停止中"
            : string.IsNullOrWhiteSpace(_settings.ProcessName) ? "対象が未設定"
            : _controller.LastError is not null ? "エラー"
            : !active ? $"{_settings.ProcessName} を待機中"
            : $"{_settings.ProcessName}: {s!.WindowCount}窓 {s.SendFps:F0}fps";
        var tip = $"WinPopCast - {text}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip; // NotifyIcon.Text は 63 文字まで
    }

    protected override void ExitThreadCore()
    {
        _timer.Dispose();
        _updateTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _form?.Dispose();
        _controller.Dispose();
        base.ExitThreadCore();
    }
}

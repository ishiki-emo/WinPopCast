using System.Diagnostics;
using System.Drawing.Imaging;
using WinPopCast.Core.Engine;
using WinPopCast.Core.Tracking;

namespace WinPopCast.App;

/// <summary>設定画面。閉じてもアプリは終了せず、トレイに戻る</summary>
public sealed class SettingsForm : Form
{
    private readonly AppController _controller;
    private readonly Action<AppSettings> _onApply;

    private readonly ComboBox _process = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly Button _refresh = new() { Text = "更新", AutoSize = true };
    private readonly ComboBox _canvas = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _fps = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly TextBox _sender = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _include = CreateRuleGrid("プロセス名（必須）");
    private readonly DataGridView _exclude = CreateRuleGrid("プロセス名");
    private readonly CheckBox _preview = new() { Text = "プレビューを表示", AutoSize = true };
    private readonly CheckBox _hideFileDialogs = new() { Text = "「ファイルを開く／保存」ダイアログを映さない（推奨）", AutoSize = true };
    private readonly PictureBox _previewBox = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(32, 32, 32),
        MinimumSize = new Size(320, 180),
    };
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button _apply = new() { Text = "適用", AutoSize = true };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private int _previewPending;

    public SettingsForm(AppController controller, AppSettings settings, Action<AppSettings> onApply)
    {
        _controller = controller;
        _onApply = onApply;

        Text = "WinPopCast 設定";
        Icon = TrayIcons.Active;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 720);
        MinimumSize = new Size(600, 600);

        BuildLayout();
        LoadSettings(settings);

        _refresh.Click += (_, _) => RefreshProcesses();
        _apply.Click += (_, _) =>
        {
            if (!ValidateInclude()) return;
            _onApply(ReadSettings());
        };
        _preview.CheckedChanged += (_, _) =>
        {
            if (!_preview.Checked) _previewBox.Image = null;
        };
        _controller.PreviewReady += OnPreview;
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        VisibleChanged += (_, _) => _controller.PreviewVisible = Visible;
        UpdateStatus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoSize = false };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var processRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        processRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        processRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        processRow.Controls.Add(_process, 0, 0);
        processRow.Controls.Add(_refresh, 1, 0);

        AddRow(root, "対象プロセス", processRow);
        AddRow(root, "キャンバス", _canvas);
        AddRow(root, "FPS", _fps);
        AddRow(root, "Spout 送信者名", _sender);
        AddRow(root, "映り込み防止", _hideFileDialogs);

        var rules = new TabControl { Dock = DockStyle.Fill, Height = 150 };
        rules.TabPages.Add(new TabPage("許可リスト（他プロセスでも映す）") { Controls = { _include } });
        rules.TabPages.Add(new TabPage("除外リスト（映さない）") { Controls = { _exclude } });
        root.Controls.Add(rules);
        root.SetColumnSpan(rules, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));

        root.Controls.Add(_preview);
        root.SetColumnSpan(_preview, 2);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(_previewBox);
        root.SetColumnSpan(_previewBox, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(_apply, 1, 0);
        root.Controls.Add(bottom);
        root.SetColumnSpan(bottom, 2);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Controls.Add(root);
        AcceptButton = _apply;

        _fps.Items.AddRange([30, 60]);
    }

    private static void AddRow(TableLayoutPanel root, string label, Control control)
    {
        root.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) });
        root.Controls.Add(control);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }

    private static DataGridView CreateRuleGrid(string processHeader)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            RowHeadersWidth = 24,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
        };
        grid.Columns.Add("ClassName", "クラス名（完全一致）");
        grid.Columns.Add("TitleContains", "タイトル（部分一致）");
        grid.Columns.Add("ProcessName", processHeader);
        return grid;
    }

    // ---- 設定の読み書き ----

    private void LoadSettings(AppSettings s)
    {
        RefreshProcesses();
        _process.Text = s.ProcessName ?? "";

        RefreshCanvasItems(s.Canvas);
        _fps.SelectedItem = s.Fps == 30 ? 30 : 60;
        _sender.Text = s.SenderName;
        FillRules(_include, s.Include);
        FillRules(_exclude, s.Exclude);
        _preview.Checked = s.Preview;
        _hideFileDialogs.Checked = s.HideFileDialogs;
    }

    private AppSettings ReadSettings()
    {
        var s = _controller.Settings?.Clone() ?? new AppSettings();
        var name = _process.Text.Trim();
        s.ProcessName = name.Length == 0 ? null : Path.GetFileNameWithoutExtension(name);
        s.Canvas = (_canvas.SelectedItem as CanvasItem)?.Value ?? "window";
        s.Fps = (int)(_fps.SelectedItem ?? 60);
        s.SenderName = string.IsNullOrWhiteSpace(_sender.Text) ? "WinPopCast" : _sender.Text.Trim();
        s.Include = ReadRules(_include);
        s.Exclude = ReadRules(_exclude);
        s.Preview = _preview.Checked;
        s.HideFileDialogs = _hideFileDialogs.Checked;
        return s;
    }

    /// <summary>
    /// 許可リストの各行にプロセス名があるか。ないと、タイトルやクラス名が一致した無関係なアプリ
    /// （ブラウザのタブなど）まで映ってしまうので、適用させない。
    /// </summary>
    private bool ValidateInclude()
    {
        foreach (DataGridViewRow row in _include.Rows)
        {
            if (row.IsNewRow) continue;
            bool any = row.Cells.Cast<DataGridViewCell>().Any(c => !string.IsNullOrWhiteSpace(c.Value?.ToString()));
            if (!any || !string.IsNullOrWhiteSpace(row.Cells[2].Value?.ToString())) continue;

            MessageBox.Show(this,
                "許可リストには、プロセス名を必ず入力してください。\n\nプロセス名がないと、タイトルやクラス名が一致した別のアプリ（ブラウザのタブなど）まで配信に映ってしまいます。",
                "WinPopCast", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _include.CurrentCell = row.Cells[2];
            _include.Focus();
            return false;
        }
        return true;
    }

    private static void FillRules(DataGridView grid, List<WindowRule> rules)
    {
        grid.Rows.Clear();
        foreach (var r in rules) grid.Rows.Add(r.ClassName ?? "", r.TitleContains ?? "", r.ProcessName ?? "");
    }

    private static List<WindowRule> ReadRules(DataGridView grid)
    {
        static string? Cell(DataGridViewRow row, int i) =>
            row.Cells[i].Value?.ToString()?.Trim() is { Length: > 0 } v ? v : null;

        var list = new List<WindowRule>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            var rule = new WindowRule { ClassName = Cell(row, 0), TitleContains = Cell(row, 1), ProcessName = Cell(row, 2) };
            if (rule.ClassName is not null || rule.TitleContains is not null || rule.ProcessName is not null) list.Add(rule);
        }
        return list;
    }

    private void RefreshProcesses()
    {
        var current = _process.Text;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.MainWindowHandle != 0 && p.Id != Environment.ProcessId) names.Add(p.ProcessName);
                }
                catch { /* 権限のないプロセスは飛ばす */ }
            }
        }
        _process.Items.Clear();
        _process.Items.AddRange([.. names]);
        _process.Text = current;
    }

    private void RefreshCanvasItems(string selected)
    {
        _canvas.Items.Clear();
        _canvas.Items.Add(new CanvasItem("window", "メインウィンドウの範囲（推奨）"));
        _canvas.Items.Add(new CanvasItem("auto", "メインウィンドウがあるモニター"));
        _canvas.Items.Add(new CanvasItem("primary", "プライマリモニター"));
        foreach (var m in Monitors.GetAll())
            _canvas.Items.Add(new CanvasItem(m.DeviceName,
                $"{m.DeviceName}  {m.Bounds.Width}x{m.Bounds.Height}  {m.Scale:P0}{(m.IsPrimary ? "（プライマリ）" : "")}"));
        _canvas.Items.Add(new CanvasItem("virtual", "全モニター（仮想デスクトップ）"));

        var match = _canvas.Items.Cast<CanvasItem>().FirstOrDefault(i => i.Value.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            // 保存されているモニターが今は接続されていない
            match = new CanvasItem(selected, $"{selected}（未接続）");
            _canvas.Items.Add(match);
        }
        _canvas.SelectedItem = match;
    }

    private sealed record CanvasItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    // ---- 状態とプレビュー ----

    private void UpdateStatus()
    {
        if (!Visible) return;
        var s = _controller.Status;
        var settings = _controller.Settings;
        string text;
        if (_controller.LastError is { } err) text = $"エラー: {err}";
        else if (settings is null || string.IsNullOrWhiteSpace(settings.ProcessName)) text = "対象プロセスを選んで「適用」を押してください";
        else if (!settings.Enabled) text = "停止中（トレイのメニューから再開できます）";
        else if (s is null) text = "起動中…";
        else if (s.TargetProcessIds.Count == 0) text = $"{settings.ProcessName} の起動を待っています";
        else
            text = $"送信中 '{settings.SenderName}'  更新 {s.SendFps:F0}/{s.TargetFps}fps  ウィンドウ {s.WindowCount}  キャンバス {s.Canvas.Width}x{s.Canvas.Height}" +
                (s.CanvasMonitorMissing ? "（指定モニターが見つからないため自動）" : "");
        if (settings?.Include.Any(r => !r.IsValidInclude) == true) text += "\n※ 許可リストのうち、プロセス名がない行は無視しています";
        if (s is { TrackerStalled: true }) text += "\n⚠ ウィンドウの追跡が止まっています。WinPopCast を再起動してください";
        if (s is { HungCaptureDisposals: > 0 }) text += $"\n※ 終了が戻らなかったキャプチャ: {s.HungCaptureDisposals} 件（動作は続けています）";
        if (_controller.LastRecovery is { } rec) text += $"\nGPU のエラーから復帰しました（{rec:HH:mm:ss}、計 {_controller.RecoveryCount} 回）";
        if (_controller.LastWarning is { } warn) text += $"\n直近のエラー: {warn}";
        _status.Text = text;
    }

    private void OnPreview(PreviewFrame frame)
    {
        // UI が追いつかないときはフレームを捨てる
        if (!_preview.Checked || Interlocked.Exchange(ref _previewPending, 1) == 1) return;
        try
        {
            BeginInvoke(() =>
            {
                try
                {
                    var bmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
                    var data = bmp.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
                    for (int y = 0; y < frame.Height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, y * frame.Width * 4, data.Scan0 + y * data.Stride, frame.Width * 4);
                    bmp.UnlockBits(data);
                    var old = _previewBox.Image;
                    _previewBox.Image = bmp;
                    old?.Dispose();
                }
                finally
                {
                    Volatile.Write(ref _previewPending, 0);
                }
            });
        }
        catch (InvalidOperationException)
        {
            Volatile.Write(ref _previewPending, 0); // ハンドル未作成・破棄済み
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; // 閉じるボタンではトレイに戻るだけ
            Hide();
            _previewBox.Image = null;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.PreviewReady -= OnPreview;
            _statusTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

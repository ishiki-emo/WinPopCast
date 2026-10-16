using System.Diagnostics;
using System.Text;
using WinPopCast.Core.Capture;
using WinPopCast.Core.Composition;
using WinPopCast.Core.Engine;
using WinPopCast.Core.Graphics;
using WinPopCast.Core.Tracking;
using WinPopCast.Spout;

// 対象プロセスの全ウィンドウを WGC でキャプチャし、キャンバス（既定はメインウィンドウのモニター）に Z 順で合成して Spout2 で送信する

Console.OutputEncoding = Encoding.UTF8;

string? processName = null;
string senderName = "WinPopCast";
int fps = 60;
bool verbose = false;
string? measurePath = null; // M4: ウィンドウごとの開始遅延を TSV に書く
var canvasSpec = CanvasSpec.Default;
string? dumpPath = null; // デバッグ: 起動 3 秒後の合成結果を PNG に保存

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} の値がありません");
    switch (args[i])
    {
        case "--process" or "-p": processName = Path.GetFileNameWithoutExtension(Next()); break;
        case "--sender": senderName = Next(); break;
        case "--fps": fps = int.Parse(Next()); break;
        case "--verbose" or "-v": verbose = true; break;
        case "--dump": dumpPath = Next(); break;
        case "--measure": measurePath = Next(); break;
        case "--canvas": canvasSpec = CanvasSpec.Parse(Next()); break;
        case "--monitors": PrintMonitors(); return 0;
        default: Console.Error.WriteLine($"不明な引数: {args[i]}"); return 1;
    }
}
if (processName is null)
{
    Console.WriteLine("""
        使い方: WinPopCast.Cli --process <名前> [オプション]
          --sender <名前>    Spout の送信者名（既定: WinPopCast）
          --fps <30|60>      出力フレームレート（既定: 60）
          --canvas <指定>    auto（メインウィンドウのモニター、既定） / virtual（全モニター） / window（メインウィンドウの矩形）
                             / primary / モニター番号（1〜） / デバイス名（\\.\DISPLAY2 など）
          --monitors         モニターの一覧を表示
          -v, --verbose      ウィンドウごとのキャプチャ開始などを表示
        """);
    return 1;
}

long t0 = Stopwatch.GetTimestamp();
void Log(string s) => Console.WriteLine($"{(Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency,9:F1} {s}");
double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
static string Describe(TrackedWindow w) => $"0x{(long)w.Handle:X} [{w.ClassName}] '{w.Title}' {w.Bounds}";

using var d3d = new D3DContext();
await WindowCapture.RequestBorderlessAccessAsync();
Log($"D3D11 OK, borderless={WindowCapture.IsBorderOptional}");

using var sender = new SpoutSender(d3d, senderName);
Log($"Spout sender '{senderName}' @ {fps}fps");

using var engine = new CaptureEngine(d3d, new EngineOptions
{
    Tracker = new TrackerOptions { ProcessNames = [processName] },
    Fps = fps,
    Canvas = canvasSpec,
}, sender);

// ウィンドウごとの記録（出現時刻・キャプチャ）。消えたときに開始遅延と表示時間を --measure に書き出す
var records = new Dictionary<nint, WindowRecord>();
using var measureLog = measurePath is null ? null : new StreamWriter(measurePath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
measureLog?.WriteLine("t_ms\tclass\ttitle\tsize\tlifetime_ms\tappear_to_start_ms\tstart_cost_ms\tstart_to_first_ms\tappear_to_first_ms\tmissed_pct\tframes");

engine.Captures.CaptureStarted += (c, w) =>
{
    lock (records) if (records.TryGetValue(c.Handle, out var r)) r.Capture = c;
    if (verbose) Log($"capture + {Describe(w)}");
};
engine.Captures.CaptureFailed += (w, ex) => Log($"capture failed {Describe(w)}: {ex.Message}");
engine.Captures.FirstFrameArrived += c =>
{
    if (!verbose) return;
    WindowRecord? r;
    lock (records) records.TryGetValue(c.Handle, out r);
    string fromAppear = r is null ? "" : $" (出現から {Ms(c.FirstFrameTimestamp - r.AppearTimestamp):F1}ms)";
    Log($"first frame {Ms(c.FirstFrameTimestamp - c.StartTimestamp),6:F1}ms{fromAppear} [{r?.Window.ClassName}] '{r?.Window.Title}'");
};

void WriteMeasure(WindowRecord r, long goneTs)
{
    if (measureLog is null) return;
    var w = r.Window;
    var c = r.Capture;
    double life = Ms(goneTs - r.AppearTimestamp);
    string F(double v) => v.ToString("F1");
    string startCost = c is null ? "" : F(Ms(c.StartedTimestamp - c.StartTimestamp));
    string appearToStart = c is null ? "" : F(Ms(c.StartTimestamp - r.AppearTimestamp));
    bool got = c is not null && c.FirstFrameTimestamp != 0 && c.FirstFrameTimestamp < goneTs;
    string startToFirst = got ? F(Ms(c!.FirstFrameTimestamp - c.StartTimestamp)) : "";
    double appearToFirst = got ? Ms(c!.FirstFrameTimestamp - r.AppearTimestamp) : life;
    double missed = life > 0 ? Math.Min(100, appearToFirst / life * 100) : 100;
    measureLog.WriteLine(string.Join('\t', F(Ms(goneTs - t0)), w.ClassName, w.Title, $"{w.Bounds.Width}x{w.Bounds.Height}",
        F(life), appearToStart, startCost, startToFirst, got ? F(appearToFirst) : "none", F(missed), c?.FrameCount ?? 0));
}

engine.Tracker.TargetProcessesChanged += set => Log(set.Count == 0 ? $"{processName} 起動待ち" : $"{processName} PID: {string.Join(", ", set)}");
engine.BeforeSync += (now, _, trigger) =>
{
    long appearTs = trigger != 0 ? trigger : now.Timestamp;
    var current = now.Windows.Select(w => w.Handle).ToHashSet();
    lock (records)
    {
        foreach (var w in now.Windows)
            if (records.TryGetValue(w.Handle, out var r)) r.Window = w; // タイトル・サイズは最新を記録する
            else records[w.Handle] = new WindowRecord(w, appearTs);
        foreach (var h in records.Keys.Where(h => !current.Contains(h)).ToList())
        {
            WriteMeasure(records[h], appearTs);
            records.Remove(h);
        }
    }
};
engine.CanvasChanged += canvas =>
{
    var main = engine.Tracker.Current.FindMainWindow();
    Log($"canvas {canvas} [{engine.Canvas}]" + (main is null ? "" : $" (main: {Describe(main)})"));
};
bool monitorMissing = false;
engine.StatusUpdated += s =>
{
    if (s.CanvasMonitorMissing != monitorMissing)
    {
        monitorMissing = s.CanvasMonitorMissing;
        if (monitorMissing) Log($"モニター '{canvasSpec.Monitor}' が見つからないため、メインウィンドウのモニターを使います");
    }
};
engine.Error += ex => Log($"error: {ex}");

if (dumpPath is not null)
{
    // デバッグ: 起動 3 秒後の合成結果（プレビュー経路を原寸で使う）を PNG に保存
    engine.PreviewMaxWidth = engine.PreviewMaxHeight = int.MaxValue;
    engine.PreviewFps = 0.3;
    var path = dumpPath;
    engine.PreviewReady += f =>
    {
        if (Ms(Stopwatch.GetTimestamp() - t0) < 3000 || Interlocked.Exchange(ref path, null) is not { } p) return;
        TextureDump.SavePngAsync(f.Pixels, f.Width, f.Height, p).ContinueWith(t =>
            Log(t.IsFaulted ? $"dump failed: {t.Exception!.InnerException!.Message}" : $"dump saved: {p}"));
    };
    engine.PreviewEnabled = true;
}

var exit = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
engine.Start();

// 5 秒ごとに状態を表示
while (!exit.Wait(5000))
{
    if (engine.Status is { } s)
        Log($"send {s.SendFps:F1}fps, render {s.RenderMs:F2}ms, windows {s.WindowCount}, captures {s.CaptureCount}, " +
            $"mem {Environment.WorkingSet / 1024 / 1024}MB");
}
return 0;

static void PrintMonitors()
{
    var all = Monitors.GetAll();
    for (int i = 0; i < all.Count; i++)
    {
        var m = all[i];
        Console.WriteLine($"  {i + 1}: {m.DeviceName,-14} {m.Bounds} dpi={m.Dpi} ({m.Scale:P0}){(m.IsPrimary ? " primary" : "")}");
    }
    Console.WriteLine($"  virtual: {Monitors.VirtualDesktop(all)}");
}

sealed class WindowRecord(TrackedWindow window, long appearTimestamp)
{
    public TrackedWindow Window { get; set; } = window;
    public long AppearTimestamp { get; } = appearTimestamp;
    public WindowCapture? Capture { get; set; }
}
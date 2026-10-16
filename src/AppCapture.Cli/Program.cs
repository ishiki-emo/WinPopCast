using System.Diagnostics;
using System.Text;
using AppCapture.Core.Capture;
using AppCapture.Core.Composition;
using AppCapture.Core.Graphics;
using AppCapture.Core.Timing;
using AppCapture.Core.Tracking;
using AppCapture.Spout;

// M3: 対象プロセスの全ウィンドウを WGC でキャプチャし、モニター範囲のキャンバスに Z 順で合成して Spout2 で送信する

Console.OutputEncoding = Encoding.UTF8;

string? processName = null;
string senderName = "AppCapture";
int fps = 60;
bool verbose = false;
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
        default: Console.Error.WriteLine($"不明な引数: {args[i]}"); return 1;
    }
}
if (processName is null)
{
    Console.WriteLine("使い方: AppCapture.Cli --process <名前> [--sender AppCapture] [--fps 60] [--verbose]");
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
using var compositor = new Compositor(d3d);
using var captures = new CaptureManager(d3d);
Log($"Spout sender '{senderName}' @ {fps}fps");

var windowInfo = new Dictionary<nint, TrackedWindow>();
captures.CaptureStarted += (c, w) => { lock (windowInfo) windowInfo[c.Handle] = w; if (verbose) Log($"capture + {Describe(w)}"); };
captures.CaptureFailed += (w, ex) => Log($"capture failed {Describe(w)}: {ex.Message}");
captures.FirstFrameArrived += c =>
{
    TrackedWindow? w;
    lock (windowInfo) windowInfo.TryGetValue(c.Handle, out w);
    if (verbose) Log($"first frame {Ms(c.FirstFrameTimestamp - c.StartTimestamp),6:F1}ms [{w?.ClassName}] '{w?.Title}'");
};

using var tracker = new WindowTracker(new TrackerOptions { ProcessNames = [processName] });
tracker.TargetProcessesChanged += set => Log(set.Count == 0 ? $"{processName} 起動待ち" : $"{processName} PID: {string.Join(", ", set)}");
tracker.SnapshotChanged += (now, _, _) => captures.Sync(now);
tracker.Start();

var exit = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Cancel(); };

// キャンバス = メインウィンドウ（owner なし・非ツールで最大のもの）があるモニター。M5 で選択式にする
static TrackedWindow? PickMain(WindowSnapshot s) =>
    s.Windows.Where(w => w.Owner == 0 && !w.IsToolWindow && w.Reason == MatchReason.TargetProcess)
        .MaxBy(w => (long)w.Bounds.Width * w.Bounds.Height);

using var pacer = new FramePacer(fps);
var layers = new List<Layer>();
RectI canvas = default;
long sent = 0, lastSent = 0, lastStats = Stopwatch.GetTimestamp();
double renderMsTotal = 0;

while (!exit.IsCancellationRequested)
{
    pacer.Wait();
    var snapshot = tracker.Current;

    if (PickMain(snapshot) is { } main && Monitors.GetBounds(main.Monitor) is { } mon && mon != canvas)
    {
        canvas = mon;
        Log($"canvas {canvas} (main: {Describe(main)})");
    }
    if (canvas.IsEmpty) continue;

    // 下（奥）から上（手前）へ。フレームが未着のウィンドウは描かない（映らない側に倒す）
    var caps = new List<(TrackedWindow W, WindowCapture C)>();
    for (int i = snapshot.Windows.Count - 1; i >= 0; i--)
        if (captures.Get(snapshot.Windows[i].Handle) is { } c) caps.Add((snapshot.Windows[i], c));

    long r0 = Stopwatch.GetTimestamp();
    lock (d3d.Lock)
    {
        layers.Clear();
        foreach (var (w, c) in caps)
            if (c.Texture is { } tex)
                layers.Add(new Layer(tex, w.Bounds.Left, w.Bounds.Top, c.ContentSize.Width, c.ContentSize.Height));

        var output = compositor.Render(canvas, layers);
        if (sender.Send(output)) sent++;

        if (dumpPath is not null && Ms(Stopwatch.GetTimestamp() - t0) > 3000)
        {
            var (px, dw, dh) = TextureDump.Read(d3d, output);
            var path = dumpPath;
            dumpPath = null;
            _ = TextureDump.SavePngAsync(px, dw, dh, path).ContinueWith(t => Log(t.IsFaulted ? $"dump failed: {t.Exception!.InnerException!.Message}" : $"dump saved: {path}"));
        }
    }
    renderMsTotal += Ms(Stopwatch.GetTimestamp() - r0);

    long now = Stopwatch.GetTimestamp();
    if (now - lastStats >= Stopwatch.Frequency * 5)
    {
        double sec = Ms(now - lastStats) / 1000;
        long n = sent - lastSent;
        Log($"send {n / sec:F1}fps, render {(n > 0 ? renderMsTotal / n : 0):F2}ms, windows {snapshot.Windows.Count}, " +
            $"captures {captures.Count}, mem {Environment.WorkingSet / 1024 / 1024}MB");
        lastSent = sent; lastStats = now; renderMsTotal = 0;
    }
}

return 0;

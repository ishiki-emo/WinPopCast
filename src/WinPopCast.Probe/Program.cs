using System.Diagnostics;
using System.Text;
using WinPopCast.Core.Interop;
using WinPopCast.Core.Tracking;

// M1: 対象プロセスのウィンドウを Z順・矩形・クラス名つきでリアルタイムにログ出力する観察ツール

Console.OutputEncoding = Encoding.UTF8;

var processNames = new List<string>();
var pids = new List<int>();
var include = new List<WindowRule>();
var exclude = new List<WindowRule>();
bool raw = false, rawAll = false, full = false, list = false;
string? logPath = null;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} の値がありません");
    switch (args[i])
    {
        case "--process" or "-p": processNames.Add(Path.GetFileNameWithoutExtension(Next())); break;
        case "--pid": pids.Add(int.Parse(Next())); break;
        case "--include":
            // <プロセス名>[:<クラス名>]。プロセス名のない許可ルールは受け付けない（無関係なアプリまで拾うため）
            var parts = Next().Split(':', 2);
            include.Add(new WindowRule { ProcessName = parts[0], ClassName = parts.Length > 1 ? parts[1] : null });
            break;
        case "--exclude-class": exclude.Add(new WindowRule { ClassName = Next() }); break;
        case "--exclude-title": exclude.Add(new WindowRule { TitleContains = Next() }); break;
        case "--raw": raw = true; break;
        case "--raw-all": raw = rawAll = true; break;
        case "--full": full = true; break;
        case "--log": logPath = Next(); break;
        case "--list": list = true; break;
        case "--help" or "-h": PrintUsage(); return 0;
        default: Console.Error.WriteLine($"不明な引数: {args[i]}"); PrintUsage(); return 1;
    }
}

if (list || (processNames.Count == 0 && pids.Count == 0))
{
    ListProcesses();
    if (!list) { Console.WriteLine(); PrintUsage(); }
    return 0;
}

using var log = logPath is null ? null : new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
var gate = new object();
long t0 = Stopwatch.GetTimestamp();

void Out(string line)
{
    var s = $"{Ms(Stopwatch.GetTimestamp()),9:F1} {line}";
    lock (gate)
    {
        Console.WriteLine(s);
        log?.WriteLine(s);
    }
}

double Ms(long ts) => (ts - t0) * 1000.0 / Stopwatch.Frequency;

var options = new TrackerOptions
{
    ProcessNames = processNames,
    ProcessIds = pids,
    Include = include,
    Exclude = exclude,
};

using var tracker = new WindowTracker(options);

tracker.TargetProcessesChanged += set =>
    Out(set.Count == 0 ? "[process] 対象プロセスなし（起動待ち）" : $"[process] 対象PID: {string.Join(", ", set)}");

if (raw)
{
    tracker.RawEvent += e =>
    {
        if (!rawAll && e.EventType == WinEvents.EVENT_OBJECT_LOCATIONCHANGE) return;
        Out($"  [event] {e.EventName,-15} {Hex(e.Handle)} pid={e.ProcessId}");
    };
}

tracker.SnapshotChanged += (now, prev, trigger) =>
{
    var sb = new StringBuilder();
    string latency = trigger != 0 ? $" (event→snapshot {(now.Timestamp - trigger) * 1000.0 / Stopwatch.Frequency:F2}ms)" : " (periodic)";
    sb.Append($"[snapshot] {now.Windows.Count} windows{latency}");

    var prevByHandle = prev.Windows.ToDictionary(w => w.Handle);
    var nowByHandle = now.Windows.ToDictionary(w => w.Handle);

    foreach (var w in now.Windows)
    {
        if (!prevByHandle.TryGetValue(w.Handle, out var old))
            sb.Append($"\n  + {Describe(w)}");
        else if (!old.SameStateAs(w))
        {
            var was = new List<string>();
            if (old.Bounds != w.Bounds) was.Add(old.Bounds.ToString());
            if (old.Title != w.Title) was.Add($"'{old.Title}'");
            if (old.Style != w.Style) was.Add($"style=0x{old.Style:X8}→0x{w.Style:X8}");
            if (old.ExStyle != w.ExStyle) was.Add($"exstyle=0x{old.ExStyle:X8}→0x{w.ExStyle:X8}");
            if (old.Owner != w.Owner) was.Add($"owner={Hex(old.Owner)}");
            if (old.Dpi != w.Dpi) was.Add($"dpi={old.Dpi}");
            sb.Append($"\n  ~ {Describe(w)}  (was {string.Join(" ", was)})");
        }
    }
    foreach (var w in prev.Windows)
        if (!nowByHandle.ContainsKey(w.Handle))
            sb.Append($"\n  - {Hex(w.Handle)} [{w.ClassName}] '{w.Title}'");

    var prevOrder = prev.Windows.Select(w => w.Handle).Where(nowByHandle.ContainsKey).ToList();
    var nowOrder = now.Windows.Select(w => w.Handle).Where(prevByHandle.ContainsKey).ToList();
    if (!prevOrder.SequenceEqual(nowOrder))
        sb.Append($"\n  z: {string.Join(" > ", now.Windows.Select(w => Hex(w.Handle)))}");

    if (full)
    {
        sb.Append("\n  ── full (top→bottom) ──");
        foreach (var w in now.Windows) sb.Append($"\n  {w.ZIndex,2} {Describe(w)}");
    }
    Out(sb.ToString());
};

Out($"tracking: {string.Join(", ", processNames.Concat(pids.Select(p => p.ToString())))}" +
    (include.Count > 0 ? $"  include: {string.Join(" | ", include)}" : "") +
    (exclude.Count > 0 ? $"  exclude: {string.Join(" | ", exclude)}" : "") +
    (logPath is not null ? $"  log: {logPath}" : ""));
Out("Ctrl+C で終了");

var exit = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
tracker.Start();
exit.Wait();
return 0;

static string Hex(nint h) => $"0x{(long)h:X}";

static string Describe(TrackedWindow w)
{
    var flags = new List<string>();
    if (w.IsPopup) flags.Add("popup");
    if (w.IsTopmost) flags.Add("topmost");
    if (w.IsToolWindow) flags.Add("tool");
    if (w.IsLayered) flags.Add("layered");
    if (w.Owner != 0) flags.Add($"owner={Hex(w.Owner)}");
    if (w.Reason == MatchReason.IncludeRule) flags.Add($"included pid={w.ProcessId}");
    if (w.Bounds != w.WindowRect) flags.Add($"wr={w.WindowRect}");
    if (w.Dpi != 96) flags.Add($"dpi={w.Dpi}");
    return $"{Hex(w.Handle)} [{w.ClassName}] '{w.Title}' {w.Bounds} {string.Join(" ", flags)}";
}

static void ListProcesses()
{
    Console.WriteLine("表示中のトップレベルウィンドウを持つプロセス:");
    foreach (var p in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
    {
        using (p)
        {
            nint main;
            try { main = p.MainWindowHandle; } catch { continue; }
            if (main == 0) continue;
            Console.WriteLine($"  {p.ProcessName,-30} pid={p.Id,-7} '{p.MainWindowTitle}'");
        }
    }
}

static void PrintUsage()
{
    Console.WriteLine("""
        使い方: WinPopCast.Probe --process <名前> [オプション]
          -p, --process <名前>       対象プロセス名（例: Unity）。複数指定可
          --pid <PID>                PIDで指定
          --include <名前>[:<クラス>] 他プロセスでも対象にするプロセス名（と、絞り込むクラス名）。IME候補窓など
          --exclude-class <クラス名>  除外するクラス名
          --exclude-title <文字列>    タイトルに含む場合は除外
          --raw                       生の WinEvent も出力（LOCATIONCHANGE を除く）
          --raw-all                   LOCATIONCHANGE も含めて出力
          --full                      変化のたびに全ウィンドウ一覧を出力
          --log <ファイル>            コンソールと同じ内容をファイルにも書く
          --list                      ウィンドウを持つプロセスの一覧
        """);
}

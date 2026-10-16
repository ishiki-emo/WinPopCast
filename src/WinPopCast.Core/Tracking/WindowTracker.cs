using System.Diagnostics;
using WinPopCast.Core.Interop;

namespace WinPopCast.Core.Tracking;

/// <summary>WinEventHook で受け取った生イベント（観察・計測用）</summary>
public readonly record struct WinEventRecord(
    uint EventType, nint Handle, int ProcessId, int IdObject, int IdChild, long Timestamp)
{
    public string EventName => WinEvents.Name(EventType);
}

/// <summary>
/// 対象プロセスのトップレベルウィンドウ集合と Z 順・矩形を追跡する。
/// 専用スレッドでメッセージループを回し、WinEventHook の通知を受けたら即座に再列挙する。
/// 取りこぼし対策として定期的にも再列挙する。
/// イベントはすべてトラッカースレッド上で発火するので、受け手はスレッドセーフにすること。
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private static readonly (uint Min, uint Max)[] HookRanges =
    [
        (WinEvents.EVENT_SYSTEM_FOREGROUND, WinEvents.EVENT_SYSTEM_MENUPOPUPEND),
        (WinEvents.EVENT_SYSTEM_MOVESIZESTART, WinEvents.EVENT_SYSTEM_MOVESIZEEND),
        (WinEvents.EVENT_SYSTEM_MINIMIZESTART, WinEvents.EVENT_SYSTEM_MINIMIZEEND),
        (WinEvents.EVENT_OBJECT_CREATE, WinEvents.EVENT_OBJECT_REORDER),
        // NAMECHANGE: Unity はウィンドウ生成後にタイトルを設定するため、タイトルによる除外ルールを即時に効かせるのに必要
        (WinEvents.EVENT_OBJECT_LOCATIONCHANGE, WinEvents.EVENT_OBJECT_NAMECHANGE),
        (WinEvents.EVENT_OBJECT_CLOAKED, WinEvents.EVENT_OBJECT_UNCLOAKED),
    ];

    private readonly TrackerOptions _options;
    private readonly List<WindowRule> _include; // 有効な許可ルールだけ（プロセス名のないものは除く）
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private readonly WinEventProc _winEventProc; // GC されないよう保持
    private readonly EnumWindowsProc _enumProc;
    private readonly List<nint> _enumBuffer = new(512);
    private readonly char[] _textBuffer = new char[512];
    private readonly Dictionary<int, string?> _processNameCache = [];

    private uint _threadId;
    private volatile bool _stopping;
    private bool _dirty = true;
    private HashSet<int>? _targetPids;
    private HashSet<nint> _trackedHandles = [];
    private long _nextRescan;
    private long _nextProcessRescan;
    private long _lastDirtyEventTimestamp;
    private Exception? _startupError;

    public WindowTracker(TrackerOptions options)
    {
        _options = options;
        _include = options.Include.Where(r => r.IsValidInclude).ToList();
        _winEventProc = OnWinEvent;
        _enumProc = (hwnd, _) => { _enumBuffer.Add(hwnd); return true; };
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "WindowTracker" };
    }

    /// <summary>最新のスナップショット（任意のスレッドから読める）</summary>
    public WindowSnapshot Current { get; private set; } = WindowSnapshot.Empty;

    /// <summary>
    /// 追跡対象が変化したとき。引数は (新, 旧, 変化のきっかけになったイベントの時刻 or 0)。
    /// </summary>
    public event Action<WindowSnapshot, WindowSnapshot, long>? SnapshotChanged;

    /// <summary>対象プロセスのウィンドウに関する生イベント（LOCATIONCHANGE を含むので量が多い）</summary>
    public event Action<WinEventRecord>? RawEvent;

    /// <summary>対象PID集合が変わったとき（起動・終了・再起動の検知）</summary>
    public event Action<IReadOnlyCollection<int>>? TargetProcessesChanged;

    public void Start()
    {
        _thread.Start();
        _started.Wait();
        if (_startupError is not null) throw new InvalidOperationException("WindowTracker failed to start", _startupError);
    }

    public void Dispose()
    {
        if (_stopping) return;
        _stopping = true;
        if (_threadId != 0) User32.PostThreadMessageW(_threadId, User32.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void ThreadMain()
    {
        var hooks = new List<nint>();
        try
        {
            _threadId = Kernel32.GetCurrentThreadId();
            // 対象PIDが複数・可変で、許可リストで他プロセスの窓も拾うため、フックはグローバルにしてコールバック側で絞る
            foreach (var (min, max) in HookRanges)
            {
                var h = User32.SetWinEventHook(min, max, 0, _winEventProc, 0, 0,
                    User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);
                if (h == 0) throw new InvalidOperationException($"SetWinEventHook failed for 0x{min:X4}-0x{max:X4}");
                hooks.Add(h);
            }
        }
        catch (Exception ex)
        {
            _startupError = ex;
        }
        _started.Set();
        if (_startupError is not null) return;

        try
        {
            RunLoop();
        }
        finally
        {
            foreach (var h in hooks) User32.UnhookWinEvent(h);
        }
    }

    private void RunLoop()
    {
        while (!_stopping)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= _nextProcessRescan) RefreshTargetProcesses(now);
            if (_dirty || now >= _nextRescan) Rescan(now);

            long waitTicks = Math.Max(0, Math.Min(_nextRescan, _nextProcessRescan) - Stopwatch.GetTimestamp());
            uint waitMs = (uint)Math.Min(waitTicks * 1000 / Stopwatch.Frequency + 1, 1000);
            User32.MsgWaitForMultipleObjectsEx(0, 0, waitMs, User32.QS_ALLINPUT, User32.MWMO_INPUTAVAILABLE);

            // WinEvent コールバックは PeekMessage の中で呼ばれる
            while (User32.PeekMessageW(out var msg, 0, 0, 0, User32.PM_REMOVE))
            {
                if (msg.message == User32.WM_QUIT) return;
                User32.TranslateMessage(msg);
                User32.DispatchMessageW(msg);
            }
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        // トップレベル判定はスナップショット側で行う。ここではウィンドウ自身のイベントだけを見る
        if (hwnd == 0 || idObject != User32.OBJID_WINDOW || idChild != User32.CHILDID_SELF)
        {
            // FOREGROUND 等は idObject が OBJID_WINDOW 以外のこともあるので通す
            if (eventType != WinEvents.EVENT_SYSTEM_FOREGROUND || hwnd == 0) return;
        }

        User32.GetWindowThreadProcessId(hwnd, out int pid); // DESTROY 後は 0 になる
        bool isTargetPid = pid != 0 && _targetPids is not null && _targetPids.Contains(pid);
        bool isTracked = _trackedHandles.Contains(hwnd);

        bool relevant = eventType switch
        {
            // Z順・表示状態の変化は他プロセス起因でも相対順に影響しうるし、許可リスト窓の出現もある
            WinEvents.EVENT_SYSTEM_FOREGROUND or WinEvents.EVENT_OBJECT_REORDER => true,
            WinEvents.EVENT_OBJECT_SHOW or WinEvents.EVENT_OBJECT_UNCLOAKED or WinEvents.EVENT_SYSTEM_MENUPOPUPSTART
                => isTargetPid || isTracked || _include.Count > 0,
            _ => isTargetPid || isTracked,
        };
        if (!relevant) return;

        long ts = Stopwatch.GetTimestamp();
        if (!_dirty) _lastDirtyEventTimestamp = ts;
        _dirty = true;

        if ((isTargetPid || isTracked) && RawEvent is { } raw)
            raw(new WinEventRecord(eventType, hwnd, pid, idObject, idChild, ts));
    }

    private void RefreshTargetProcesses(long now)
    {
        _nextProcessRescan = now + (long)(_options.ProcessRescanInterval.TotalSeconds * Stopwatch.Frequency);

        var pids = new HashSet<int>(_options.ProcessIds);
        foreach (var name in _options.ProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                pids.Add(p.Id);
                p.Dispose();
            }
        }

        if (_targetPids is null || !pids.SetEquals(_targetPids))
        {
            _targetPids = pids;
            _processNameCache.Clear(); // PID 再利用に備える
            _dirty = true;
            TargetProcessesChanged?.Invoke(pids);
        }
    }

    private void Rescan(long now)
    {
        long trigger = _dirty ? _lastDirtyEventTimestamp : 0;
        _dirty = false;
        _nextRescan = now + (long)(_options.RescanInterval.TotalSeconds * Stopwatch.Frequency);

        _enumBuffer.Clear();
        User32.EnumWindows(_enumProc, 0); // 列挙順 = Z順（上から下）

        var list = new List<TrackedWindow>();
        foreach (var hwnd in _enumBuffer)
        {
            var w = Inspect(hwnd, list.Count);
            if (w is not null) list.Add(w);
        }

        var snapshot = new WindowSnapshot(list, Stopwatch.GetTimestamp(), _targetPids ?? []);
        var previous = Current;
        if (previous.ContentEquals(snapshot)) return;

        Current = snapshot;
        _trackedHandles = list.Select(w => w.Handle).ToHashSet();
        SnapshotChanged?.Invoke(snapshot, previous, trigger);
    }

    private TrackedWindow? Inspect(nint hwnd, int zIndex)
    {
        User32.GetWindowThreadProcessId(hwnd, out int pid);
        bool isTarget = _targetPids is not null && _targetPids.Contains(pid);
        if (!isTarget && _include.Count == 0) return null;

        if (!User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd)) return null;
        if (Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return null;

        uint style = (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE);
        if ((style & TrackedWindow.WS_CHILD) != 0) return null;

        string className = GetText(hwnd, isClass: true);
        string title = GetText(hwnd, isClass: false);
        string? ProcName() => GetProcessName(pid);

        if (_options.Exclude.Any(r => r.Matches(className, title, ProcName))) return null;

        MatchReason reason;
        if (isTarget) reason = MatchReason.TargetProcess;
        else if (_include.Any(r => r.Matches(className, title, ProcName))) reason = MatchReason.IncludeRule;
        else return null;

        User32.GetWindowRect(hwnd, out var wr);
        var windowRect = new RectI(wr.Left, wr.Top, wr.Right, wr.Bottom);
        var bounds = Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT efb, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0
            ? new RectI(efb.Left, efb.Top, efb.Right, efb.Bottom)
            : windowRect;
        if (bounds.IsEmpty) return null;

        return new TrackedWindow(
            hwnd, pid, className, title, bounds, windowRect, zIndex,
            Owner: User32.GetWindow(hwnd, User32.GW_OWNER),
            Style: style,
            ExStyle: (uint)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE),
            Dpi: User32.GetDpiForWindow(hwnd),
            Monitor: User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST),
            Reason: reason);
    }

    private string GetText(nint hwnd, bool isClass)
    {
        int len = isClass
            ? User32.GetClassNameW(hwnd, _textBuffer, _textBuffer.Length)
            : User32.InternalGetWindowText(hwnd, _textBuffer, _textBuffer.Length);
        return len > 0 ? new string(_textBuffer, 0, len) : "";
    }

    private string? GetProcessName(int pid)
    {
        if (_processNameCache.TryGetValue(pid, out var name)) return name;
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
        }
        catch
        {
            name = null;
        }
        _processNameCache[pid] = name;
        return name;
    }
}

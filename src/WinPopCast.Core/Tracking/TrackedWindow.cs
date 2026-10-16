namespace WinPopCast.Core.Tracking;

/// <summary>物理ピクセル（仮想デスクトップ座標）の矩形</summary>
public readonly record struct RectI(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public override string ToString() => $"({Left},{Top} {Width}x{Height})";
}

public enum MatchReason
{
    /// <summary>対象プロセスに属する</summary>
    TargetProcess,
    /// <summary>許可リストにより他プロセスから追加された（IME候補窓など）</summary>
    IncludeRule,
}

/// <summary>追跡中のトップレベルウィンドウ1枚の状態</summary>
public sealed record TrackedWindow(
    nint Handle,
    int ProcessId,
    string ClassName,
    string Title,
    RectI Bounds, // DWMWA_EXTENDED_FRAME_BOUNDS（見えないリサイズ枠を除いた矩形）
    RectI WindowRect, // GetWindowRect の矩形（比較・検証用）
    int ZIndex, // 0 が最前面。追跡対象内での順位
    nint Owner,
    uint Style,
    uint ExStyle,
    uint Dpi,
    nint Monitor,
    MatchReason Reason)
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public bool IsPopup => (Style & WS_POPUP) != 0;
    public bool IsTopmost => (ExStyle & WS_EX_TOPMOST) != 0;
    public bool IsToolWindow => (ExStyle & WS_EX_TOOLWINDOW) != 0;
    public bool IsLayered => (ExStyle & WS_EX_LAYERED) != 0;

    /// <summary>位置・内容が同じか（Z順は比較しない）</summary>
    public bool SameStateAs(TrackedWindow other) =>
        Handle == other.Handle && Bounds == other.Bounds && Title == other.Title &&
        Style == other.Style && ExStyle == other.ExStyle && Dpi == other.Dpi && Owner == other.Owner;
}

/// <summary>ある時点での追跡対象ウィンドウ一覧（Z順・上から下）</summary>
public sealed class WindowSnapshot
{
    public static readonly WindowSnapshot Empty = new([], 0, []);

    public WindowSnapshot(IReadOnlyList<TrackedWindow> windows, long timestamp, IReadOnlyCollection<int> targetProcessIds)
    {
        Windows = windows;
        Timestamp = timestamp;
        TargetProcessIds = targetProcessIds;
    }

    /// <summary>Z順で上（手前）から下</summary>
    public IReadOnlyList<TrackedWindow> Windows { get; }

    /// <summary>Stopwatch.GetTimestamp() の値</summary>
    public long Timestamp { get; }

    public IReadOnlyCollection<int> TargetProcessIds { get; }

    /// <summary>メインウィンドウ = 対象プロセスの、owner を持たない非ツールウィンドウのうち最大のもの</summary>
    public TrackedWindow? FindMainWindow() =>
        Windows.Where(w => w.Owner == 0 && !w.IsToolWindow && w.Reason == MatchReason.TargetProcess)
            .MaxBy(w => (long)w.Bounds.Width * w.Bounds.Height);

    public bool ContentEquals(WindowSnapshot other)
    {
        if (Windows.Count != other.Windows.Count) return false;
        for (int i = 0; i < Windows.Count; i++)
            if (!Windows[i].SameStateAs(other.Windows[i])) return false;
        return true;
    }
}

namespace WinPopCast.Core.Tracking;

/// <summary>
/// ウィンドウの許可・除外ルール。指定したフィールドがすべて一致したときにマッチする。
/// 文字列比較は大文字小文字を区別しない。
/// </summary>
public sealed class WindowRule
{
    /// <summary>クラス名の完全一致</summary>
    public string? ClassName { get; init; }

    /// <summary>タイトルの部分一致</summary>
    public string? TitleContains { get; init; }

    /// <summary>プロセス名（拡張子なし）の完全一致</summary>
    public string? ProcessName { get; init; }

    internal bool Matches(string className, string title, Func<string?> processName)
    {
        if (ClassName is null && TitleContains is null && ProcessName is null) return false;
        if (ClassName is not null && !string.Equals(ClassName, className, StringComparison.OrdinalIgnoreCase)) return false;
        if (TitleContains is not null && !title.Contains(TitleContains, StringComparison.OrdinalIgnoreCase)) return false;
        if (ProcessName is not null && !string.Equals(ProcessName, processName(), StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public override string ToString() =>
        string.Join(" ", new[]
        {
            ClassName is null ? null : $"class={ClassName}",
            TitleContains is null ? null : $"title~{TitleContains}",
            ProcessName is null ? null : $"process={ProcessName}",
        }.Where(s => s is not null));
}

public sealed class TrackerOptions
{
    /// <summary>対象プロセス名（拡張子なし。例: "Unity"）。同名プロセスはすべて対象</summary>
    public IReadOnlyList<string> ProcessNames { get; init; } = [];

    /// <summary>PIDで直接指定する場合</summary>
    public IReadOnlyList<int> ProcessIds { get; init; } = [];

    /// <summary>対象プロセス以外でも映すウィンドウ</summary>
    public IReadOnlyList<WindowRule> Include { get; init; } = [];

    /// <summary>対象プロセスでも映さないウィンドウ（Include より優先）</summary>
    public IReadOnlyList<WindowRule> Exclude { get; init; } = [];

    /// <summary>イベント取りこぼし対策の定期再列挙間隔</summary>
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>プロセス名から PID を引き直す間隔（再起動時の自動再接続用）</summary>
    public TimeSpan ProcessRescanInterval { get; init; } = TimeSpan.FromSeconds(2);
}

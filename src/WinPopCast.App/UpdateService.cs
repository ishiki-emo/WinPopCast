using Velopack;
using Velopack.Sources;

namespace WinPopCast.App;

/// <summary>
/// GitHub Releases から新しい版を確認する（Velopack）。
/// 配信中に勝手に再起動しないよう、確認と通知だけを自動で行い、適用はユーザーが選んだときだけにする。
/// インストーラーで入れていない（zip 版・開発中の）ときは何もしない。
/// </summary>
internal sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/ishiki-emo/WinPopCast";

    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;
    private bool _busy;

    public UpdateService()
    {
        // 動作確認用：環境変数でローカルのフォルダーや別の URL を更新元にできる
        var overrideSource = Environment.GetEnvironmentVariable("WINPOPCAST_UPDATE_SOURCE");
        _manager = string.IsNullOrWhiteSpace(overrideSource)
            ? new UpdateManager(new GithubSource(RepositoryUrl, null, false, null))
            : new UpdateManager(overrideSource);
    }

    /// <summary>インストーラーで入れた版か（zip 版では更新を扱わない）</summary>
    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>見つかった新しい版（なければ null）</summary>
    public string? AvailableVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>新しい版が見つかったとき（UI スレッドで発火）</summary>
    public event Action<string>? UpdateAvailable;

    public string? LastError { get; private set; }

    /// <summary>新しい版を確認する。見つかれば UpdateAvailable を発火する</summary>
    public async Task CheckAsync()
    {
        if (!IsInstalled || _busy) return;
        _busy = true;
        string? found = null;
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            LastError = null;
            if (info is null) return;
            if (_pending?.TargetFullRelease.Version != info.TargetFullRelease.Version)
                found = info.TargetFullRelease.Version.ToString();
            _pending = info;
        }
        catch (Exception ex)
        {
            // オフラインなど。次の確認でやり直す
            LastError = ex.Message;
        }
        finally
        {
            _busy = false;
        }
        // 受け手がそのまま更新を始められるよう、処理中の状態を解いてから通知する
        if (found is not null) UpdateAvailable?.Invoke(found);
    }

    /// <summary>ダウンロードして適用し、アプリを再起動する（ユーザーが選んだときだけ呼ぶ）</summary>
    public async Task DownloadAndRestartAsync(Action<int>? progress = null)
    {
        if (_pending is null || _busy) return;
        _busy = true;
        try
        {
            await _manager.DownloadUpdatesAsync(_pending, progress);
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        finally
        {
            _busy = false;
        }
    }
}

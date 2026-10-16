using Microsoft.Win32;

namespace WinPopCast.App;

/// <summary>Windows の起動時に自動で起動する（HKCU の Run キー。管理者権限は要らない）</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinPopCast";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>
    /// 有効にすると、今の実行ファイルを登録する。インストーラー版は更新しても同じ場所（current フォルダー）で
    /// 起動するので、登録し直す必要はない
    /// </summary>
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルの場所がわかりません");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

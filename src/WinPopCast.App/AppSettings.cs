using System.Text.Encodings.Web;
using System.Text.Json;
using WinPopCast.Core.Tracking;

namespace WinPopCast.App;

public sealed class AppSettings
{
    /// <summary>対象プロセス名（拡張子なし）。実行ファイル名で記憶し、再起動後も自動で再接続する</summary>
    public string? ProcessName { get; set; }

    public string SenderName { get; set; } = "WinPopCast";

    public int Fps { get; set; } = 60;

    /// <summary>CanvasSpec の文字列表現（auto / virtual / window / primary / デバイス名）</summary>
    public string Canvas { get; set; } = "auto";

    public List<WindowRule> Include { get; set; } = [];

    public List<WindowRule> Exclude { get; set; } = [];

    /// <summary>送信するか（トレイのメニューで切り替える）</summary>
    public bool Enabled { get; set; } = true;

    public bool Preview { get; set; } = true;

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    // ---- 保存と読み込み ----

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま書く
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPopCast", "settings.json");

    /// <summary>旧名（AppCapture）のときの保存先</summary>
    private static string LegacyPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppCapture", "settings.json");

    /// <summary>
    /// 新しい保存先に設定がなく、旧名のときの設定があれば引き継ぐ。旧ファイルは消さない。
    /// 送信者名が旧既定値のままなら新しい既定値に変える（OBS 側では送信者を選び直す必要がある）。
    /// </summary>
    public static void MigrateLegacy()
    {
        if (File.Exists(DefaultPath) || !File.Exists(LegacyPath)) return;
        var settings = Load(LegacyPath);
        if (settings.SenderName == "AppCapture") settings.SenderName = new AppSettings().SenderName;
        settings.Save(DefaultPath);
    }

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // 壊れた設定は退避して既定値で起動する
            File.Copy(path, path + ".broken", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}

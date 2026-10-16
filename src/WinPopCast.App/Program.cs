namespace WinPopCast.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 同じ送信者名で 2 つ動くと Spout が混乱するので、多重起動はさせない
        using var mutex = new Mutex(true, @"Local\WinPopCast.App", out bool created);
        if (!created)
        {
            MessageBox.Show("WinPopCast はすでに起動しています。タスクトレイのアイコンから設定を開けます。", "WinPopCast",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        ApplicationConfiguration.Initialize();

        string settingsPath = AppSettings.DefaultPath;
        bool showSettings = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--settings" && i + 1 < args.Length) settingsPath = args[++i];
            else if (args[i] == "--show") showSettings = true;
        }

        if (settingsPath == AppSettings.DefaultPath) AppSettings.MigrateLegacy();
        Application.Run(new TrayApp(settingsPath, showSettings));
        return 0;
    }
}

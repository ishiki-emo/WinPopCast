namespace WinPopCast.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // インストーラー（Velopack）からのインストール・更新・アンインストールの呼び出しを処理する。必ず最初に呼ぶ
        Velopack.VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => { try { AutoStart.Set(false); } catch { } })
            .Run();

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
        int testDeviceLost = 0;
        bool testUpdate = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--settings" && i + 1 < args.Length) settingsPath = args[++i];
            else if (args[i] == "--show") showSettings = true;
            else if (args[i] == "--test-device-lost" && i + 1 < args.Length) testDeviceLost = int.Parse(args[++i]); // 復帰処理の確認用（非公開）
            else if (args[i] == "--test-update") testUpdate = true; // 更新の確認用：見つかった更新を確認なしで適用する（非公開）
        }

        if (settingsPath == AppSettings.DefaultPath) AppSettings.MigrateLegacy();
        Application.Run(new TrayApp(settingsPath, showSettings, testDeviceLost, testUpdate));
        return 0;
    }
}

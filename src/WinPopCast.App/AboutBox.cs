using System.Diagnostics;
using System.Reflection;

namespace WinPopCast.App;

/// <summary>バージョン情報・著作権表示・第三者のライセンスへの案内</summary>
internal static class AboutBox
{
    private const string RepositoryUrl = "https://github.com/ishiki-emo/WinPopCast";

    public static void Show()
    {
        var asm = Assembly.GetExecutingAssembly();
        string version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? asm.GetName().Version?.ToString() ?? "";
        string copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

        var text = $"""
            WinPopCast {version}
            {copyright}
            {RepositoryUrl}

            WinPopCast 自身は MIT License で提供しています。

            次の第三者のソフトウェアを含んでおり、それぞれのライセンスが適用されます。
              ・Spout2 SDK（BSD 2-Clause） © Lynn Jarvis
              ・Vortice.Windows / Vortice.Mathematics（MIT） © Amer Koleci and Contributors
              ・SharpGen.Runtime（MIT）
              ・Velopack（MIT） © Velopack Ltd
              ・Microsoft.Windows.SDK.NET / WinRT.Runtime（Windows SDK ライセンス） © Microsoft Corporation
              ・.NET / Windows Forms（MIT） © .NET Foundation and Contributors

            ライセンスの全文を開きますか？
            """;

        if (MessageBox.Show(text, "WinPopCast について", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            return;

        // 配布 zip では exe と同じフォルダーにある。なければ GitHub の表記を開く
        string local = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        string target = File.Exists(local) ? local : $"{RepositoryUrl}/blob/master/THIRD-PARTY-NOTICES.md";
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"開けませんでした: {target}\n{ex.Message}", "WinPopCast", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

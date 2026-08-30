using System.IO;

namespace SimpleVidDownload.Services;

/// <summary>
/// Tìm thư mục bin\ chứa engine (yt-dlp, ffmpeg, deno) và các thư mục làm việc.
/// Khi chạy từ src\bin\Debug\... thì exe nằm sâu, nên phải dò ngược lên các thư mục cha.
/// </summary>
public static class AppPaths
{
    public static string EngineDir { get; }
    public static string AppRoot { get; }

    public static string YtDlp => Path.Combine(EngineDir, "yt-dlp.exe");
    public static string LogDir => Path.Combine(AppRoot, "logs");
    public static string WebViewData => Path.Combine(AppRoot, "wvdata");
    public static string SettingsFile => Path.Combine(AppRoot, "settings.json");

    static AppPaths()
    {
        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // 1) bin\ ngay cạnh exe   2) chính thư mục exe   3) dò ngược lên tối đa 6 cấp
        var found = Probe(Path.Combine(exeDir, "bin")) ?? Probe(exeDir);
        if (found is null)
        {
            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 6 && dir?.Parent != null; i++)
            {
                dir = dir.Parent;
                found = Probe(Path.Combine(dir.FullName, "bin"));
                if (found != null) break;
            }
        }

        EngineDir = found ?? Path.Combine(exeDir, "bin");
        AppRoot = Directory.GetParent(EngineDir)?.FullName ?? exeDir;

        Directory.CreateDirectory(LogDir);
    }

    private static string? Probe(string dir) =>
        File.Exists(Path.Combine(dir, "yt-dlp.exe")) ? dir : null;

    public static bool EngineReady => File.Exists(YtDlp);
}

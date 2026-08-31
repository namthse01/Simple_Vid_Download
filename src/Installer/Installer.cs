using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using Microsoft.Win32;

namespace DCDownloadSetup;

public record Step(string Text, double Percent);

/// <summary>
/// Cài đặt DCDownload: bung app ra thư mục đích, tải engine, tạo shortcut, ghi mục gỡ cài đặt.
///
/// App được nhúng sẵn trong file cài (bản tự chứa .NET runtime nên máy đích không cần cài gì trước),
/// còn engine (yt-dlp, ffmpeg, deno, AI) thì tải lúc cài — gộp hết vào sẽ thành file cài gần 500 MB.
/// </summary>
public static class Installer
{
    public const string AppName = "DCDownload";
    public const string DisplayName = "DCDownload — DragonCloud Download";
    public const string Version = "2.1.0";
    private const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DCDownload";

    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", AppName);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static async Task InstallAsync(
        string targetDir, bool desktopShortcut, bool withAi,
        IProgress<Step> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        var binDir = Path.Combine(targetDir, "bin");
        Directory.CreateDirectory(binDir);

        // 1. bung app đã nhúng sẵn
        progress.Report(new Step("Đang cài DCDownload...", 2));
        var exePath = Path.Combine(targetDir, "DCDownload.exe");
        await ExtractPayloadAsync(exePath, ct);

        // 2. giữ lại chính file cài này làm bộ gỡ cài đặt
        var uninstaller = Path.Combine(targetDir, "uninstall.exe");
        try { File.Copy(Environment.ProcessPath!, uninstaller, true); } catch { }

        // 3. tải engine
        await GetFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
            Path.Combine(binDir, "yt-dlp.exe"), "yt-dlp", 5, 18, progress, ct);

        await GetZipAsync("https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip",
            binDir, new[] { "ffmpeg.exe", "ffprobe.exe" }, "ffmpeg (nặng nhất, hơi lâu)", 18, 62, progress, ct);

        await GetZipAsync("https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip",
            binDir, new[] { "deno.exe" }, "deno", 62, 80, progress, ct);

        if (withAi) await GetEsrganAsync(binDir, progress, ct);

        // 4. shortcut
        progress.Report(new Step("Đang tạo shortcut...", 94));
        CreateShortcut(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", AppName + ".lnk"),
            exePath, targetDir);
        if (desktopShortcut)
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"),
                exePath, targetDir);

        // 5. ghi vào danh sách gỡ cài đặt của Windows
        progress.Report(new Step("Đang hoàn tất...", 98));
        RegisterUninstall(targetDir, exePath, uninstaller);

        progress.Report(new Step("Xong!", 100));
    }

    private static async Task ExtractPayloadAsync(string dest, CancellationToken ct)
    {
        var asm = Assembly.GetExecutingAssembly();
        await using var src = asm.GetManifestResourceStream("payload.exe")
            ?? throw new InvalidOperationException(
                "File cài này thiếu phần app bên trong (chạy build-installer.ps1 để đóng gói lại).");
        await using var fs = File.Create(dest);
        await src.CopyToAsync(fs, ct);
    }

    private static async Task GetFileAsync(string url, string dest, string label,
        double from, double to, IProgress<Step> progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = File.Create(dest);
        var buf = new byte[81920];
        long done = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            double frac = total > 0 ? (double)done / total : 0;
            var sizeText = total > 0
                ? $"{done / 1024 / 1024} MB / {total / 1024 / 1024} MB"
                : $"{done / 1024 / 1024} MB";
            progress.Report(new Step($"Đang tải {label}... {sizeText}", from + (to - from) * frac));
        }
    }

    private static async Task GetZipAsync(string url, string binDir, string[] wanted, string label,
        double from, double to, IProgress<Step> progress, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "dcd_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        try
        {
            var zip = Path.Combine(tmp, "a.zip");
            await GetFileAsync(url, zip, label, from, to - 4, progress, ct);

            progress.Report(new Step($"Đang giải nén {label}...", to - 3));
            var ex = Path.Combine(tmp, "x");
            ZipFile.ExtractToDirectory(zip, ex, true);
            foreach (var name in wanted)
            {
                var found = Directory.GetFiles(ex, name, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) File.Copy(found, Path.Combine(binDir, name), true);
            }
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    private static async Task GetEsrganAsync(string binDir, IProgress<Step> progress, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "dcd_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        try
        {
            var zip = Path.Combine(tmp, "es.zip");
            await GetFileAsync(
                "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip",
                zip, "công cụ AI upscale", 80, 90, progress, ct);

            progress.Report(new Step("Đang giải nén công cụ AI...", 92));
            var ex = Path.Combine(tmp, "x");
            ZipFile.ExtractToDirectory(zip, ex, true);

            var dst = Path.Combine(binDir, "realesrgan");
            Directory.CreateDirectory(dst);
            foreach (var f in new[] { "realesrgan-ncnn-vulkan.exe", "vcomp140.dll" })
            {
                var found = Directory.GetFiles(ex, f, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) File.Copy(found, Path.Combine(dst, f), true);
            }
            var models = Directory.GetDirectories(ex, "models", SearchOption.AllDirectories).FirstOrDefault();
            if (models != null)
            {
                var mdst = Path.Combine(dst, "models");
                Directory.CreateDirectory(mdst);
                foreach (var m in Directory.GetFiles(models))
                    File.Copy(m, Path.Combine(mdst, Path.GetFileName(m)), true);
            }
        }
        catch { /* thiếu AI thì app vẫn chạy, chỉ là không có mục nâng cấp */ }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    private static void CreateShortcut(string lnkPath, string target, string workDir)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return;
            dynamic? sh = Activator.CreateInstance(t);
            if (sh is null) return;
            dynamic lnk = sh.CreateShortcut(lnkPath);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = workDir;
            lnk.IconLocation = target + ",0";
            lnk.Description = DisplayName;
            lnk.Save();
        }
        catch { }
    }

    private static void RegisterUninstall(string dir, string exePath, string uninstaller)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RegKey);
            if (k is null) return;
            long size = 0;
            try
            {
                size = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { }
            k.SetValue("DisplayName", DisplayName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "namthse01");
            k.SetValue("DisplayIcon", exePath);
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
            k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch { }
    }

    // ================= gỡ cài đặt =================

    public static string? InstalledDir()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegKey);
            return k?.GetValue("InstallLocation") as string;
        }
        catch { return null; }
    }

    public static void Uninstall()
    {
        var dir = InstalledDir();

        foreach (var lnk in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", AppName + ".lnk")
        })
        {
            try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
        }

        try { Registry.CurrentUser.DeleteSubKeyTree(RegKey, false); } catch { }

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        // uninstall.exe đang chạy nằm trong chính thư mục cần xoá,
        // nên nhờ cmd đợi vài giây rồi mới xoá sau khi tiến trình này thoát
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                "/c timeout /t 2 /nobreak >nul & rd /s /q \"" + dir + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { }
    }
}

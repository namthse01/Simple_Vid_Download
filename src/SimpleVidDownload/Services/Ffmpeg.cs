using System.Diagnostics;
using System.IO;
using System.Text;

namespace SimpleVidDownload.Services;

/// <summary>
/// Ghép hình + tiếng từ hai file rời thành một MP4. Chỉ đóng gói lại (-c copy), không mã hoá
/// lại nên vài giây là xong. Cần cho Facebook: DASH của họ tách hình và tiếng thành hai stream.
/// </summary>
public static class Ffmpeg
{
    public static string Exe => Path.Combine(AppPaths.EngineDir, "ffmpeg.exe");

    public static async Task<(bool ok, string log)> MergeAsync(string videoPath, string audioPath, string outPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppPaths.AppRoot
        };
        string[] args =
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", videoPath, "-i", audioPath,
            "-map", "0:v:0", "-map", "1:a:0", "-c", "copy",
            "-movflags", "+faststart",          // moov lên đầu: mở phát được ngay, kể cả khi stream
            outPath
        ];
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (false, "Không chạy được ffmpeg.");
            var err = p.StandardError.ReadToEndAsync();
            var outp = p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var log = await outp + await err;
            return (p.ExitCode == 0 && File.Exists(outPath), log);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace SimpleVidDownload.Services;

public record UpscaleProgress(double Percent, string Stage, TimeSpan? Eta);

public record VideoInfo(int Width, int Height, double Fps, double Duration);

/// <summary>
/// Nâng độ phân giải bằng AI (Real-ESRGAN ncnn-vulkan).
///
/// Vì sao xử lý theo TỪNG ĐOẠN chứ không rã cả video ra ảnh: một video 30 phút ở 25fps là
/// 45.000 khung, lưu PNG 1080p thì hơn 100 GB. Cắt thành đoạn ~10 giây, xử lý xong đoạn nào
/// xoá ảnh đoạn đó, nên chỗ trống cần chỉ khoảng 1 GB bất kể video dài bao nhiêu.
/// </summary>
public static class Upscaler
{
    private const int SegmentSeconds = 10;

    public static string ToolDir => Path.Combine(AppPaths.EngineDir, "realesrgan");
    public static string ToolExe => Path.Combine(ToolDir, "realesrgan-ncnn-vulkan.exe");
    public static string FfmpegExe => Path.Combine(AppPaths.EngineDir, "ffmpeg.exe");
    public static string FfprobeExe => Path.Combine(AppPaths.EngineDir, "ffprobe.exe");

    public static bool ToolAvailable =>
        File.Exists(ToolExe) && Directory.Exists(Path.Combine(ToolDir, "models"));

    /// <summary>Kết quả dò khả năng GPU, lưu lại để khỏi dò lại mỗi lần mở app.</summary>
    public static bool? GpuOk { get; private set; }

    /// <summary>
    /// Dò xem GPU có chạy nổi không bằng cách nâng thử một ảnh 64x64.
    /// Đây là cách chắc ăn nhất: có Vulkan chưa chắc đã chạy được (driver cũ, GPU quá yếu).
    /// </summary>
    public static async Task<bool> ProbeGpuAsync(CancellationToken ct = default)
    {
        if (GpuOk.HasValue) return GpuOk.Value;
        if (!ToolAvailable) { GpuOk = false; return false; }

        var dir = Path.Combine(Path.GetTempPath(), "dcd_gpuprobe_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            var src = Path.Combine(dir, "in.png");
            var dst = Path.Combine(dir, "out.png");

            // tạo ảnh thử bằng ffmpeg cho khỏi phải nhúng file vào app
            var made = await RunAsync(FfmpegExe,
                ["-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=64x64", "-frames:v", "1", "-y", src],
                TimeSpan.FromSeconds(20), ct);
            if (!made.ok || !File.Exists(src)) { GpuOk = false; return false; }

            var up = await RunAsync(ToolExe,
                ["-i", src, "-o", dst, "-n", "realesr-animevideov3-x2", "-s", "2", "-f", "png"],
                TimeSpan.FromSeconds(60), ct);

            GpuOk = up.ok && File.Exists(dst) && new FileInfo(dst).Length > 0;
            return GpuOk.Value;
        }
        catch
        {
            GpuOk = false;
            return false;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    public static async Task<VideoInfo?> ProbeVideoAsync(string path, CancellationToken ct = default)
    {
        var r = await RunAsync(FfprobeExe,
            ["-v", "error", "-select_streams", "v:0",
             "-show_entries", "stream=width,height,r_frame_rate",
             "-show_entries", "format=duration",
             "-of", "default=noprint_wrappers=1", path],
            TimeSpan.FromSeconds(30), ct);
        if (!r.ok) return null;

        int w = 0, h = 0; double fps = 0, dur = 0;
        foreach (var line in r.output.Split('\n'))
        {
            var kv = line.Trim().Split('=', 2);
            if (kv.Length != 2) continue;
            switch (kv[0])
            {
                case "width": int.TryParse(kv[1], out w); break;
                case "height": int.TryParse(kv[1], out h); break;
                case "duration": double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out dur); break;
                case "r_frame_rate":
                    var f = kv[1].Split('/');
                    if (f.Length == 2 && double.TryParse(f[0], out var n) && double.TryParse(f[1], out var d) && d > 0)
                        fps = n / d;
                    break;
            }
        }
        return w > 0 && h > 0 ? new VideoInfo(w, h, fps > 0 ? fps : 25, dur) : null;
    }

    /// <summary>Đích gợi ý theo đúng ý người dùng: dưới 1080 thì đề xuất 1080, từ 1080 trở lên cho chọn cao hơn.</summary>
    public static int SuggestTarget(int sourceHeight) =>
        sourceHeight < 1080 ? 1080 : (sourceHeight < 1440 ? 1440 : 2160);

    /// <summary>
    /// Nâng video lên chiều cao yêu cầu. Trả về đường dẫn file mới, hoặc null nếu hỏng/bị hủy.
    /// File gốc được giữ nguyên.
    /// </summary>
    public static async Task<string?> UpscaleAsync(
        string input, int targetHeight,
        IProgress<UpscaleProgress>? progress, CancellationToken ct)
    {
        var info = await ProbeVideoAsync(input, ct);
        if (info is null || info.Height <= 0) return null;
        if (info.Height >= targetHeight) return null;   // không cần nâng

        // model chỉ có x2/x3/x4 -> chọn mức nhỏ nhất đủ cao, rồi thu về đúng chiều cao đích
        int scale = (int)Math.Ceiling((double)targetHeight / info.Height);
        scale = Math.Clamp(scale, 2, 4);
        var model = $"realesr-animevideov3-x{scale}";

        var work = Path.Combine(Path.GetDirectoryName(input)!, ".dcd_upscale_" + Guid.NewGuid().ToString("N")[..8]);
        var segDir = Path.Combine(work, "seg");
        var outDir = Path.Combine(work, "out");
        Directory.CreateDirectory(segDir);
        Directory.CreateDirectory(outDir);

        var started = DateTime.Now;
        try
        {
            // 1. cắt thành đoạn nhỏ (chỉ sao chép luồng, rất nhanh)
            progress?.Report(new UpscaleProgress(0, "split", null));
            var split = await RunAsync(FfmpegExe,
                ["-v", "error", "-i", input, "-c", "copy", "-f", "segment",
                 "-segment_time", SegmentSeconds.ToString(), "-reset_timestamps", "1",
                 Path.Combine(segDir, "s_%04d.mp4")],
                TimeSpan.FromMinutes(10), ct);
            if (!split.ok) return null;

            var segs = Directory.GetFiles(segDir, "s_*.mp4").OrderBy(x => x).ToList();
            if (segs.Count == 0) return null;

            double totalFrames = Math.Max(1, info.Duration * info.Fps);
            double doneFrames = 0;
            var finals = new List<string>();

            // 2. từng đoạn: rã ảnh -> AI -> đóng lại -> xoá ảnh
            for (int i = 0; i < segs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var fin = Path.Combine(work, "f_in");
                var fout = Path.Combine(work, "f_out");
                Recreate(fin);
                Recreate(fout);

                await RunAsync(FfmpegExe,
                    ["-v", "error", "-i", segs[i], "-fps_mode", "passthrough",
                     Path.Combine(fin, "%08d.png")],
                    TimeSpan.FromMinutes(10), ct);

                int frames = Directory.GetFiles(fin, "*.png").Length;
                if (frames == 0) continue;

                // chạy AI, vừa chạy vừa đếm ảnh đã xong để báo tiến độ
                var aiTask = RunAsync(ToolExe,
                    ["-i", fin, "-o", fout, "-n", model, "-s", scale.ToString(), "-f", "png"],
                    TimeSpan.FromHours(3), ct);

                while (!aiTask.IsCompleted)
                {
                    await Task.Delay(700, ct);
                    int made = SafeCount(fout);
                    double p = (doneFrames + made) / totalFrames;
                    progress?.Report(new UpscaleProgress(
                        Math.Clamp(p * 100, 0, 99), "ai", EstimateEta(started, p)));
                }
                var ai = await aiTask;
                if (!ai.ok) return null;

                // đóng lại thành video, lấy tiếng từ đoạn gốc
                var outSeg = Path.Combine(outDir, $"o_{i:D4}.mp4");
                var enc = await RunAsync(FfmpegExe,
                    ["-v", "error",
                     "-framerate", info.Fps.ToString("0.###", CultureInfo.InvariantCulture),
                     "-i", Path.Combine(fout, "%08d.png"),
                     "-i", segs[i],
                     "-map", "0:v:0", "-map", "1:a:0?",
                     "-vf", $"scale=-2:{targetHeight}:flags=lanczos",
                     "-c:v", "libx264", "-crf", "18", "-preset", "medium", "-pix_fmt", "yuv420p",
                     "-c:a", "aac", "-b:a", "192k", "-shortest", "-y", outSeg],
                    TimeSpan.FromHours(1), ct);
                if (!enc.ok) return null;

                finals.Add(outSeg);
                doneFrames += frames;
                Directory.Delete(fin, true);
                Directory.Delete(fout, true);
            }

            if (finals.Count == 0) return null;

            // 3. nối các đoạn lại
            progress?.Report(new UpscaleProgress(99, "join", null));
            var listFile = Path.Combine(work, "list.txt");
            await File.WriteAllTextAsync(listFile,
                string.Join('\n', finals.Select(f => "file '" + f.Replace("'", @"'\''") + "'")),
                new UTF8Encoding(false), ct);

            var dir = Path.GetDirectoryName(input)!;
            var name = Path.GetFileNameWithoutExtension(input);
            var result = Path.Combine(dir, $"{name}_{targetHeight}p.mp4");
            int dup = 2;
            while (File.Exists(result))
                result = Path.Combine(dir, $"{name}_{targetHeight}p_{dup++}.mp4");

            var join = await RunAsync(FfmpegExe,
                ["-v", "error", "-f", "concat", "-safe", "0", "-i", listFile, "-c", "copy", "-y", result],
                TimeSpan.FromMinutes(30), ct);
            if (!join.ok || !File.Exists(result)) return null;

            progress?.Report(new UpscaleProgress(100, "done", TimeSpan.Zero));
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private static TimeSpan? EstimateEta(DateTime started, double fraction)
    {
        if (fraction <= 0.01) return null;
        var elapsed = DateTime.Now - started;
        var total = elapsed.TotalSeconds / fraction;
        var left = total - elapsed.TotalSeconds;
        return left > 0 ? TimeSpan.FromSeconds(left) : TimeSpan.Zero;
    }

    private static int SafeCount(string dir)
    {
        try { return Directory.GetFiles(dir, "*.png").Length; } catch { return 0; }
    }

    private static void Recreate(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
    }

    private static async Task<(bool ok, string output)> RunAsync(
        string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppPaths.AppRoot
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };

        try
        {
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await p.WaitForExitAsync(cts.Token);
            string text; lock (sb) text = sb.ToString();
            return (p.ExitCode == 0, text);
        }
        catch
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            string text; lock (sb) text = sb.ToString();
            return (false, text);
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SimpleVidDownload.Services;

public class YtDlpResult
{
    public bool Cancelled { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string StdErr { get; init; } = "";
    /// <summary>Tên file cuối cùng lấy được từ log (chỉ tên, để hiện cho người dùng).</summary>
    public string SavedFile { get; init; } = "";
    /// <summary>Đường dẫn đầy đủ tới file đã lưu — dùng cho nút "Mở video".</summary>
    public string SavedPath { get; init; } = "";
}

/// <summary>
/// Chạy yt-dlp và đọc tiến trình theo thời gian thực.
/// Hai chỗ dễ sập đã xử lý sẵn:
///  - Phải truyền --encoding utf-8 VÀ đọc luồng ra bằng UTF-8, nếu không tên tiếng Việt
///    trong log sẽ rụng hết dấu (yt-dlp mặc định ghi theo bảng mã ANSI của Windows).
///  - Tên file cuối phải lấy từ dòng "[Merger] Merging formats into", vì dòng
///    "Destination:" cuối cùng trỏ vào file tạm (.f251.webm) chứ không phải file cuối.
/// </summary>
public class YtDlpRunner
{
    private Process? _proc;
    private bool _cancelled;
    private readonly StringBuilder _out = new();
    private readonly StringBuilder _err = new();

    public event Action<string>? LineReceived;
    public event Action<double>? ProgressChanged;
    public event Action<string>? StageChanged;

    public bool IsRunning => _proc is { HasExited: false };

    private static readonly Regex RxPercent = new(@"\[download\]\s+([0-9.]+)%", RegexOptions.Compiled);
    private static readonly Regex RxStage = new(@"^\[(Merger|ExtractAudio|VideoConvertor|FixupM3u8)\]", RegexOptions.Compiled);
    // Multiline là bắt buộc: log có nhiều dòng, không có nó thì ^ $ chỉ khớp đầu/cuối cả chuỗi
    private static readonly Regex RxDestination = new(@"^\[[^\]]+\] Destination:\s*(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex RxMerging = new(@"^\[Merger\] Merging formats into\s+""(.+?)""\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex RxAlready = new(@"^\[download\]\s+(.+?)\s+has already been downloaded",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public async Task<YtDlpResult> RunAsync(IEnumerable<string> args)
    {
        _cancelled = false;
        _out.Clear();
        _err.Clear();

        var full = new List<string> { "--encoding", "utf-8" };
        full.AddRange(args);

        var psi = new ProcessStartInfo
        {
            FileName = AppPaths.YtDlp,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppPaths.AppRoot
        };
        foreach (var a in full) psi.ArgumentList.Add(a);

        // để yt-dlp thấy deno.exe + ffmpeg.exe (deno cần cho YouTube chất lượng cao)
        psi.Environment["PATH"] = AppPaths.EngineDir + ";" + Environment.GetEnvironmentVariable("PATH");

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_out) _out.AppendLine(e.Data);
            HandleLine(e.Data);
        };
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_err) _err.AppendLine(e.Data);
        };

        _proc.Start();
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();
        await _proc.WaitForExitAsync();

        var exitCode = _proc.ExitCode;
        string outText, errText;
        lock (_out) outText = _out.ToString();
        lock (_err) errText = _err.ToString();

        WriteLog(outText, errText);

        var savedPath = ExtractSavedPath(outText);
        return new YtDlpResult
        {
            Cancelled = _cancelled,
            Success = !_cancelled && exitCode == 0,
            ExitCode = exitCode,
            StdErr = errText,
            SavedPath = savedPath,
            SavedFile = savedPath.Length > 0 ? Path.GetFileName(savedPath) : ""
        };
    }

    public void Cancel()
    {
        if (_proc is null || _proc.HasExited) return;
        _cancelled = true;
        try { _proc.Kill(entireProcessTree: true); }
        catch { }
    }

    private void HandleLine(string line)
    {
        LineReceived?.Invoke(line);

        var m = RxPercent.Match(line);
        if (m.Success && double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            ProgressChanged?.Invoke(pct);
            return;
        }

        if (RxStage.IsMatch(line))
        {
            StageChanged?.Invoke(Loc.T("merging"));
            return;
        }

        var trimmed = line.Trim();
        if (trimmed.Length > 0)
            StageChanged?.Invoke(trimmed.Length > 110 ? trimmed[..110] + "..." : trimmed);
    }

    /// <summary>Dò đường dẫn file cuối cùng trong log. Thứ tự ưu tiên có chủ đích.</summary>
    private static string ExtractSavedPath(string log)
    {
        string saved = "";
        foreach (Match m in RxDestination.Matches(log)) saved = m.Groups[1].Value;
        // file đã có sẵn từ trước -> yt-dlp không ghi dòng Destination nào
        foreach (Match m in RxAlready.Matches(log)) saved = m.Groups[1].Value;
        // dòng Merger ghi đè: nó mới là file cuối, Destination cuối chỉ là file tạm (.f251.webm)
        foreach (Match m in RxMerging.Matches(log)) saved = m.Groups[1].Value;
        return saved;
    }

    private static void WriteLog(string outText, string errText)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.WriteAllText(Path.Combine(AppPaths.LogDir, $"out_{stamp}.log"), outText, Encoding.UTF8);
            if (errText.Length > 0)
                File.WriteAllText(Path.Combine(AppPaths.LogDir, $"err_{stamp}.log"), errText, Encoding.UTF8);
        }
        catch { }
    }

    /// <summary>
    /// Hỏi nguồn xem có sẵn độ phân giải cao nhất là bao nhiêu (không tải gì cả).
    /// Trả về 0 nếu không xác định được.
    /// Dùng để khỏi phải nâng bằng AI khi nguồn vốn đã có sẵn bản nét hơn.
    /// </summary>
    public static async Task<int> ProbeMaxHeightAsync(
        string url, bool useCookie, string browser, CapturedLink? captured,
        CancellationToken ct = default)
    {
        var a = new List<string>
        {
            "--encoding", "utf-8", "--no-warnings", "--simulate", "--no-playlist",
            "-f", "bestvideo/best", "--print", "%(height)s"
        };
        if (useCookie) { a.Add("--cookies-from-browser"); a.Add(browser); }
        if (captured != null && captured.Url == url)
        {
            if (!string.IsNullOrEmpty(captured.Referer)) { a.Add("--referer"); a.Add(captured.Referer); }
            if (!string.IsNullOrEmpty(captured.UserAgent)) { a.Add("--user-agent"); a.Add(captured.UserAgent); }
            if (!string.IsNullOrEmpty(captured.Cookie)) { a.Add("--add-header"); a.Add("Cookie: " + captured.Cookie); }
        }
        a.Add(url);

        var psi = new ProcessStartInfo
        {
            FileName = AppPaths.YtDlp,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppPaths.AppRoot
        };
        foreach (var x in a) psi.ArgumentList.Add(x);
        psi.Environment["PATH"] = AppPaths.EngineDir + ";" + Environment.GetEnvironmentVariable("PATH");

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return 0;
            var outText = await p.StandardOutput.ReadToEndAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(90));
            await p.WaitForExitAsync(cts.Token);

            foreach (var line in outText.Split('\n'))
                if (int.TryParse(line.Trim(), out var h) && h > 0)
                    return h;
        }
        catch { }
        return 0;
    }

    /// <summary>Dựng tham số tải theo lựa chọn trên giao diện.</summary>
    /// <param name="overrideHeight">Nếu có, dùng chiều cao này thay cho mục chất lượng đã chọn.</param>
    public static List<string> BuildDownloadArgs(
        string url, string folder, int qualityIndex, bool playlist,
        bool useCookie, string browser, CapturedLink? captured, string? titleForFile,
        int? overrideHeight = null)
    {
        var a = new List<string>
        {
            "--newline", "--no-mtime", "--windows-filenames",
            "--ffmpeg-location", AppPaths.EngineDir
        };

        var template = !string.IsNullOrEmpty(titleForFile)
            ? Path.Combine(folder, titleForFile + ".%(ext)s")
            : Path.Combine(folder, "%(title)s.%(ext)s");
        a.Add("-o");
        a.Add(template);

        // Chọn theo độ phân giải, và ƯU TIÊN CODEC cho dễ phát:
        //  - Từ 1440p trở lên YouTube chỉ có VP9 hoặc AV1 (H.264 dừng ở 1080p).
        //    Ưu tiên VP9 vì AV1 nhiều máy chưa có bộ giải mã, tải về sẽ không phát được.
        //  - Từ 1080p trở xuống ưu tiên H.264 (avc1) vì máy nào cũng phát được.
        // Ghép ra MP4 chỉ là đóng gói lại, KHÔNG mã hoá lại nên rất nhanh.
        void Video(int maxHeight, string codec)
        {
            a.AddRange([
                "-f", $"bestvideo[height<={maxHeight}]+bestaudio/best[height<={maxHeight}]/best",
                "-S", $"res:{maxHeight},vcodec:{codec}",
                "--merge-output-format", "mp4"
            ]);
        }

        if (overrideHeight is int oh)
        {
            // dùng khi bật nâng cấp AI: đích do người dùng chọn quyết định, không theo ô chất lượng
            Video(oh, oh >= 1440 ? "vp9" : "avc1");
        }
        else
        {
            switch (qualityIndex)
            {
                case 0: Video(2160, "vp9"); break;   // 4K nếu có, không có thì tự lùi xuống
                case 1: Video(1440, "vp9"); break;
                case 2: Video(1080, "avc1"); break;
                case 3: Video(720, "avc1"); break;
                case 4: Video(480, "avc1"); break;
                case 5:
                    a.AddRange(["-x", "--audio-format", "mp3", "--audio-quality", "0"]);
                    break;
            }
        }

        if (!playlist) a.Add("--no-playlist");

        if (useCookie)
        {
            a.Add("--cookies-from-browser");
            a.Add(browser);
        }

        // link bắt từ trình duyệt: gắn đúng header của phiên đó để máy chủ không chặn
        if (captured != null && captured.Url == url)
        {
            if (!string.IsNullOrEmpty(captured.Referer)) { a.Add("--referer"); a.Add(captured.Referer); }
            if (!string.IsNullOrEmpty(captured.UserAgent)) { a.Add("--user-agent"); a.Add(captured.UserAgent); }
            if (!string.IsNullOrEmpty(captured.Cookie)) { a.Add("--add-header"); a.Add("Cookie: " + captured.Cookie); }
        }

        a.Add(url);
        return a;
    }
}

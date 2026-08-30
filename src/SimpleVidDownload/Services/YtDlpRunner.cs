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
    /// <summary>Tên file cuối cùng lấy được từ log (nếu có).</summary>
    public string SavedFile { get; init; } = "";
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

        return new YtDlpResult
        {
            Cancelled = _cancelled,
            Success = !_cancelled && exitCode == 0,
            ExitCode = exitCode,
            StdErr = errText,
            SavedFile = ExtractSavedFile(outText)
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
            StageChanged?.Invoke("Đang ghép / chuyển đổi file...");
            return;
        }

        var trimmed = line.Trim();
        if (trimmed.Length > 0)
            StageChanged?.Invoke(trimmed.Length > 110 ? trimmed[..110] + "..." : trimmed);
    }

    private static string ExtractSavedFile(string log)
    {
        string saved = "";
        foreach (Match m in RxDestination.Matches(log)) saved = m.Groups[1].Value;
        // dòng Merger ghi đè: nó mới là file cuối, Destination cuối chỉ là file tạm
        foreach (Match m in RxMerging.Matches(log)) saved = m.Groups[1].Value;
        return saved.Length > 0 ? Path.GetFileName(saved) : "";
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

    /// <summary>Dựng tham số tải theo lựa chọn trên giao diện.</summary>
    public static List<string> BuildDownloadArgs(
        string url, string folder, int qualityIndex, bool playlist,
        bool useCookie, string browser, CapturedLink? captured, string? titleForFile)
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

        switch (qualityIndex)
        {
            case 0:
                a.AddRange(["-f", "bestvideo+bestaudio/best", "--merge-output-format", "mp4"]);
                break;
            case 1:
                a.AddRange(["-f", "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best", "--merge-output-format", "mp4"]);
                break;
            case 2:
                a.AddRange(["-f", "bestvideo[height<=720]+bestaudio/best[height<=720]/best", "--merge-output-format", "mp4"]);
                break;
            case 3:
                a.AddRange(["-f", "bestvideo[height<=480]+bestaudio/best[height<=480]/best", "--merge-output-format", "mp4"]);
                break;
            case 4:
                a.AddRange(["-x", "--audio-format", "mp3", "--audio-quality", "0"]);
                break;
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

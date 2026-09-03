using System.Text;
using System.Text.RegularExpressions;

namespace SimpleVidDownload.Services;

/// <summary>Một link video bắt được, kèm header của chính phiên duyệt đã tải nó.</summary>
/// <param name="Note">Ghi chú thêm cho người dùng (ví dụ "720p") — không dùng để quyết định gì.</param>
public record CapturedLink(string Kind, string Url, string Referer, string Cookie, string UserAgent,
                           string Note = "")
{
    public string Host
    {
        get
        {
            try { return new Uri(Url).Host; }
            catch { return ""; }
        }
    }

    /// <summary>Dòng hiển thị trong danh sách: [LOẠI ghi-chú] host   link-rút-gọn</summary>
    public string Display
    {
        get
        {
            var shortUrl = Url.Length > 110 ? Url[..110] + "..." : Url;
            var tag = Note.Length > 0 ? $"{Kind} {Note}" : Kind;
            return $"[{tag}] {Host}   {shortUrl}";
        }
    }
}

/// <summary>
/// Nhận diện link video trong luồng mạng của trình duyệt nhúng.
/// Hai bài học quan trọng rút ra khi làm bản PowerShell:
///  - Phải nhận diện theo cả Content-Type, vì nhiều stream không có đuôi .mp4/.m3u8
///    (ví dụ googlevideo trả về đường dẫn "videoplayback" trống trơn).
///  - Phải lọc host quảng cáo và các mảnh stream lẻ, không thì danh sách ngập rác.
/// </summary>
public static class MediaSniffer
{
    public const string HLS = "HLS";
    public const string DASH = "DASH";
    public const string MP4 = "MP4";
    public const string AUDIO = "AUDIO";
    public const string EMBED = "NHUNG";

    // Facebook / Instagram phát video theo từng khúc byte: cùng một file nhưng mỗi request kèm
    // bytestart=..&byteend=.. khác nhau. Tải đúng link đó thì chỉ được một khúc giữa file —
    // yt-dlp báo xong mà không player nào mở được. Bỏ hai tham số này đi là ra link cả file.
    private static readonly Regex RangeParam = new(
        @"[?&](bytestart|byteend)=\d*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Link này là một khúc byte của file lớn hơn?</summary>
    public static bool IsRangeChunk(string url) => RangeParam.IsMatch(url);

    /// <summary>Bỏ bytestart/byteend để link trỏ vào cả file. Link thường thì trả về nguyên.</summary>
    public static string StripRange(string url)
    {
        if (!RangeParam.IsMatch(url)) return url;
        var s = RangeParam.Replace(url, "");
        // xoá mất tham số đầu tiên thì dấu "&" kế tiếp phải thành "?"
        if (!s.Contains('?') && s.Contains('&'))
        {
            int i = s.IndexOf('&');
            s = s[..i] + "?" + s[(i + 1)..];
        }
        return s;
    }

    // Facebook mô tả từng stream trong tham số efg = base64 của JSON {"vencode_tag":"..."}:
    // "dash_ln_heaac_vbr3_audio" là tiếng, "dash_vp9-basic-gen2_720p" là hình 720p.
    private static readonly Regex EfgParam = new(@"[?&]efg=([^&#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeightTag = new(@"(\d{3,4})p\b", RegexOptions.Compiled);

    private static string DecodeEfg(string url)
    {
        var m = EfgParam.Match(url);
        if (!m.Success) return "";
        try
        {
            var b64 = Uri.UnescapeDataString(m.Groups[1].Value).Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }
        catch { return ""; }
    }

    /// <summary>Stream chỉ có tiếng (Facebook tách hình và tiếng thành hai file DASH riêng).</summary>
    public static bool LooksLikeAudio(string url) =>
        DecodeEfg(url).Contains("audio", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ghi chú ngắn cho người dùng, ví dụ "720p". Không đoán được thì rỗng.</summary>
    public static string QualityNote(string url)
    {
        var m = HeightTag.Match(DecodeEfg(url));
        return m.Success ? m.Groups[1].Value + "p" : "";
    }

    private static readonly Regex VideoIdTag = new(@"""video_id""\s*:\s*""?(\d+)", RegexOptions.Compiled);

    /// <summary>Mã video Facebook ghi trong efg — hình và tiếng của cùng một video mang cùng mã.</summary>
    public static string VideoId(string url)
    {
        var m = VideoIdTag.Match(DecodeEfg(url));
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>Chiều cao đọc từ ghi chú ("720p" → 720), 0 nếu không có.</summary>
    private static int NoteHeight(CapturedLink l)
    {
        var m = HeightTag.Match(l.Note);
        return m.Success && int.TryParse(m.Groups[1].Value, out var h) ? h : 0;
    }

    private static readonly Regex AdHosts = new(
        @"vietadx|doubleclick|googlesyndication|adsystem|adnxs|popads|exoclick|juicyads|trafficjunky",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SegmentFile = new(
        @"\.(ts|m4s)(\?|#|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SegmentName = new(
        @"(seg|segment|chunk|frag)[-_/]?\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Trang player trung gian: yt-dlp không đọc được, phải mở bằng trình duyệt mới ra link thật.</summary>
    private static readonly Regex EmbedPage = new(
        @"blogger\.com/video\.g", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsEmbedPage(string url) => EmbedPage.IsMatch(url);

    public static bool ShouldSkip(string url) =>
        string.IsNullOrEmpty(url)
        || url.Contains("/cdn-cgi/", StringComparison.OrdinalIgnoreCase)
        || AdHosts.IsMatch(url)
        || SegmentFile.IsMatch(url)
        || SegmentName.IsMatch(url);

    /// <summary>Đoán loại từ hình dạng URL.</summary>
    public static string? KindFromUrl(string url)
    {
        if (Regex.IsMatch(url, @"\.m3u8(\?|#|$)", RegexOptions.IgnoreCase)) return HLS;
        if (Regex.IsMatch(url, @"\.mpd(\?|#|$)", RegexOptions.IgnoreCase)) return DASH;
        if (Regex.IsMatch(url, @"\.(mp4|m4v|webm|mov)(\?|#|$)", RegexOptions.IgnoreCase)) return MP4;
        if (url.Contains("googlevideo.com/videoplayback", StringComparison.OrdinalIgnoreCase)) return MP4;
        if (EmbedPage.IsMatch(url)) return EMBED;
        return null;
    }

    /// <summary>Đoán loại từ Content-Type của phản hồi — bắt được cả link không có đuôi file.</summary>
    public static string? KindFromContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return null;
        if (contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)) return HLS;
        if (contentType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase)) return DASH;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return MP4;
        return null;
    }

    /// <summary>
    /// Ưu tiên khi người dùng chưa chọn gì: HLS &gt; MP4 &gt; còn lại (link nhúng xếp cuối).
    /// Nhiều MP4 có ghi chú độ phân giải thì lấy bản nét nhất; không thì lấy bản đến sau cùng.
    /// Link chỉ có tiếng không bao giờ tự được chọn.
    /// </summary>
    public static CapturedLink? PickBest(IReadOnlyList<CapturedLink> links)
    {
        if (links.Count == 0) return null;
        var hls = links.FirstOrDefault(l => l.Kind == HLS);
        if (hls != null) return hls;

        CapturedLink? best = null;
        foreach (var l in links)
        {
            if (l.Kind != MP4) continue;
            if (best is null || NoteHeight(l) >= NoteHeight(best)) best = l;
        }
        return best ?? links.LastOrDefault(l => l.Kind != AUDIO) ?? links[^1];
    }

    /// <summary>
    /// Tìm stream tiếng đi cặp với stream hình đã chọn (Facebook tách hình/tiếng).
    /// Có mã video thì bắt buộc cùng mã — trang có nhiều video (story, feed) mà ghép nhầm
    /// tiếng của video khác thì tệ hơn là không có tiếng. Không có mã thì lấy stream gần nhất.
    /// </summary>
    public static CapturedLink? PairedAudio(IReadOnlyList<CapturedLink> links, CapturedLink video)
    {
        var id = VideoId(video.Url);
        if (id.Length > 0)
            return links.LastOrDefault(l => l.Kind == AUDIO && VideoId(l.Url) == id);

        int at = -1;
        for (int i = 0; i < links.Count; i++)
            if (links[i].Url == video.Url) { at = i; break; }
        if (at < 0) return null;

        CapturedLink? best = null;
        int bestDist = int.MaxValue;
        for (int i = 0; i < links.Count; i++)
        {
            if (links[i].Kind != AUDIO) continue;
            int d = Math.Abs(i - at);
            if (d < bestDist) { bestDist = d; best = links[i]; }
        }
        return best;
    }

    /// <summary>Bỏ ký tự Windows cấm để dùng tiêu đề trang làm tên file.</summary>
    public static string SafeFileName(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var cleaned = Regex.Replace(title, @"[\\/:*?""<>|%]", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length > 120 ? cleaned[..120].Trim() : cleaned;
    }
}

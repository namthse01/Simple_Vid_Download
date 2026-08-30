using System.Text.RegularExpressions;

namespace SimpleVidDownload.Services;

/// <summary>Một link video bắt được, kèm header của chính phiên duyệt đã tải nó.</summary>
public record CapturedLink(string Kind, string Url, string Referer, string Cookie, string UserAgent)
{
    public string Host
    {
        get
        {
            try { return new Uri(Url).Host; }
            catch { return ""; }
        }
    }

    /// <summary>Dòng hiển thị trong danh sách: [LOẠI] host   link-rút-gọn</summary>
    public string Display
    {
        get
        {
            var shortUrl = Url.Length > 110 ? Url[..110] + "..." : Url;
            return $"[{Kind}] {Host}   {shortUrl}";
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
    public const string EMBED = "NHUNG";

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

    /// <summary>Ưu tiên khi người dùng chưa chọn gì: HLS &gt; MP4 &gt; còn lại (link nhúng xếp cuối).</summary>
    public static CapturedLink? PickBest(IReadOnlyList<CapturedLink> links)
    {
        if (links.Count == 0) return null;
        return links.FirstOrDefault(l => l.Kind == HLS)
            ?? links.LastOrDefault(l => l.Kind == MP4)
            ?? links[^1];
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

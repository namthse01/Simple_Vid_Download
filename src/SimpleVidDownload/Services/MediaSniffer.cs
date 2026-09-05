using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace SimpleVidDownload.Services;

/// <summary>Một link video bắt được, kèm header của chính phiên duyệt đã tải nó.</summary>
/// <param name="Note">Ghi chú thêm cho người dùng (ví dụ "720p") — không dùng để quyết định gì.</param>
public record CapturedLink(string Kind, string Url, string Referer, string Cookie, string UserAgent,
                           string Note = "") : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // record mặc định so sánh bằng GIÁ TRỊ của mọi trường — kể cả Bytes/LastActivity vốn bị
    // Touch() sửa liên tục khi video phát. ListBox ghi nhớ dòng đã chọn theo cách so sánh đó,
    // nên item vừa đổi giá trị là nó không nhận ra nữa: không bỏ chọn được dòng cũ (dòng nào
    // cũng xanh), và hỏi "đang chọn dòng nào" thì trả lời không có. Mỗi link là một đối tượng
    // riêng, so sánh theo tham chiếu mới đúng.
    public virtual bool Equals(CapturedLink? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    /// <summary>
    /// Lần gần nhất trang xin một khúc "ra tấm ra món" của link này. Video đang phát thì được xin
    /// liên tục; video trang tải sẵn cho lượt sau chỉ bị xin vài trăm byte đầu.
    /// </summary>
    public DateTime LastActivity { get; private set; } = DateTime.Now;
    /// <summary>Tổng byte trang đã xin của link này (theo bytestart/byteend); 0 nếu không biết.</summary>
    public long Bytes { get; private set; }
    /// <summary>Đang là link hoạt động gần nhất trong danh sách — hiện dấu ▶ cho người dùng.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Trang vừa xin thêm một khúc của link này.</summary>
    public void Touch(long bytes, bool substantial)
    {
        if (bytes > 0) Bytes += bytes;
        if (substantial) LastActivity = DateTime.Now;
        Notify();
    }

    public void SetActive(bool on)
    {
        if (IsActive == on) return;
        IsActive = on;
        Notify();
    }

    // DisplayMemberPath="Display" nghe sự kiện này -> dòng tự cập nhật, không phải dựng lại danh sách
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));

    public string Host
    {
        get
        {
            try { return new Uri(Url).Host; }
            catch { return ""; }
        }
    }

    /// <summary>Dòng hiển thị trong danh sách: ▶ [LOẠI ghi-chú] dung-lượng host   link-rút-gọn</summary>
    public string Display
    {
        get
        {
            var shortUrl = Url.Length > 110 ? Url[..110] + "..." : Url;
            var tag = Note.Length > 0 ? $"{Kind} {Note}" : Kind;
            var size = Bytes >= 100_000 ? $"{Bytes / 1048576.0:0.0} MB  " : "";
            var mark = IsActive ? "▶ " : "";
            return $"{mark}[{tag}] {size}{Host}   {shortUrl}";
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

    private static readonly Regex RangeStart = new(@"[?&]bytestart=(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RangeEnd = new(@"[?&]byteend=(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Độ dài khúc byte trang xin (byteend − bytestart + 1); -1 nếu link không có range (xin cả file).</summary>
    public static long RangeLength(string url)
    {
        var s = RangeStart.Match(url);
        var e = RangeEnd.Match(url);
        if (!s.Success || !e.Success) return -1;
        if (!long.TryParse(s.Groups[1].Value, out var a) || !long.TryParse(e.Groups[1].Value, out var b)) return -1;
        return b >= a ? b - a + 1 : 0;
    }

    /// <summary>
    /// Khúc từ cỡ này trở lên mới coi là "đang phát". Khúc khởi tạo / mục lục chỉ vài trăm byte,
    /// và video trang tải sẵn cho lượt kế cũng chỉ bị xin chừng đó.
    /// </summary>
    public const long SubstantialBytes = 16 * 1024;

    public static bool IsSubstantial(long rangeLength) => rangeLength < 0 || rangeLength >= SubstantialBytes;

    private static readonly Regex LongNumber = new(@"\d{10,}", RegexOptions.Compiled);

    /// <summary>
    /// Mọi mã số dài trong địa chỉ trang, kể cả mã giấu trong đoạn base64 (story Facebook:
    /// /stories/&lt;bucket&gt;/UzpfSVNDOjQ1...= giải ra "S:_ISC:4549365958631252"). Dùng để
    /// nhận ra stream nào là của chính trang đang xem.
    /// </summary>
    public static HashSet<string> IdsInPageUrl(string pageUrl)
    {
        var ids = new HashSet<string>();
        if (string.IsNullOrEmpty(pageUrl)) return ids;
        string plain;
        try { plain = Uri.UnescapeDataString(pageUrl); } catch { plain = pageUrl; }

        foreach (Match m in LongNumber.Matches(plain)) ids.Add(m.Value);

        foreach (var seg in plain.Split('/', '?', '&', '#'))
        {
            var b64 = seg.TrimEnd('=').Replace('-', '+').Replace('_', '/');
            if (b64.Length < 12 || !Regex.IsMatch(b64, @"^[A-Za-z0-9+/]+$")) continue;
            try
            {
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                var text = Encoding.ASCII.GetString(Convert.FromBase64String(b64));
                if (text.Any(c => c < ' ' || c > '~')) continue;   // không phải chữ -> không phải mã
                foreach (Match m in LongNumber.Matches(text)) ids.Add(m.Value);
            }
            catch { }
        }
        return ids;
    }

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

    // "dash_vp9-basic-gen2_720p" -> 720p;  "xpv_progressive.FACEBOOK..C3.360.sve_sd" -> 360p
    private static readonly Regex HeightDotted = new(@"\.(\d{3,4})\.", RegexOptions.Compiled);

    /// <summary>Ghi chú ngắn cho người dùng, ví dụ "720p trọn bộ". Không đoán được thì rỗng.</summary>
    public static string QualityNote(string url)
    {
        var efg = DecodeEfg(url);
        var m = HeightTag.Match(efg);
        if (!m.Success) m = HeightDotted.Match(efg);
        var note = m.Success ? m.Groups[1].Value + "p" : "";
        if (ProgressiveTag.IsMatch(efg)) note = (note + " trọn bộ").Trim();
        return note;
    }

    // Facebook có HAI kiểu stream, phân biệt bằng vencode_tag trong efg:
    //  - "dash_..." : tai san luc mo trang, hinh va tieng TACH thanh hai file, phai ghep
    //  - "xpv_progressive..." : xin dung luc nguoi dung chuyen tOi the do, va la MOT file
    //    tron bo (tieng nam san trong). Uu tien kieu nay: vua dung the dang xem, vua khoi ghep.
    private static readonly Regex ProgressiveTag = new(
        @"xpv_progressive", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Link kiểu "trọn bộ": một file có sẵn cả hình và tiếng, không cần ghép.</summary>
    public static bool IsProgressive(string url) => ProgressiveTag.IsMatch(DecodeEfg(url));

    private static readonly Regex VideoIdTag = new(@"""video_id""\s*:\s*""?(\d+)", RegexOptions.Compiled);
    // link tron bo khong co video_id, thay bang xpv_asset_id
    private static readonly Regex AssetIdTag = new(@"""xpv_asset_id""\s*:\s*""?(\d+)", RegexOptions.Compiled);
    private static readonly Regex DurTag = new(@"""duration_s""\s*:\s*(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>Độ dài video (giây) ghi trong efg; 0 nếu không có.</summary>
    public static double DurationSeconds(string url)
    {
        var m = DurTag.Match(DecodeEfg(url));
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    /// <summary>
    /// Tìm stream ứng với video đang hiện trên trang, nhận diện bằng ĐỘ DÀI. Cần cho story
    /// nhiều thẻ: thẻ kế được tải sẵn từ lúc mở, chuyển qua là phát từ bộ đệm — không có
    /// request mạng nào nữa để mà đoán. Độ dài thẻ đang phát (hỏi qua DevTools) so với
    /// duration_s trong efg; lệch ≤1.5s coi là cùng video, nhiều bản thì lấy bản nét nhất.
    /// </summary>
    public static CapturedLink? ByDuration(IReadOnlyList<CapturedLink> links, double seconds)
    {
        if (seconds < 1 || double.IsNaN(seconds)) return null;
        var same = links.Where(l => l.Kind == MP4)
            .Where(l =>
            {
                var d = DurationSeconds(l.Url);
                return d > 0 && Math.Abs(d - seconds) <= 1.5;
            })
            .ToList();
        if (same.Count == 0) return null;

        // story hay có nhiều thẻ dài y nhau -> bản trọn bộ (chỉ có khi đang xem thẻ đó) là chắc nhất
        var prog = same.Where(l => IsProgressive(l.Url)).ToList();
        if (prog.Count > 0) return prog.OrderByDescending(l => l.LastActivity).First();
        return Sharpest(same);
    }

    /// <summary>Mã video Facebook ghi trong efg — hình và tiếng của cùng một video mang cùng mã.</summary>
    public static string VideoId(string url)
    {
        var efg = DecodeEfg(url);
        var m = VideoIdTag.Match(efg);
        if (m.Success) return m.Groups[1].Value;
        m = AssetIdTag.Match(efg);
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
    /// Trang có nhiều video (story Facebook tự chuyển và tải sẵn video kế) thì:
    ///  1. stream mang mã trùng với địa chỉ trang đang xem — chắc nhất;
    ///  2. không có thì stream đang phát (được xin khúc gần nhất).
    /// Cùng một video có nhiều bản thì lấy bản nét nhất. Link chỉ có tiếng không bao giờ tự được chọn.
    /// </summary>
    public static CapturedLink? PickBest(IReadOnlyList<CapturedLink> links, string pageUrl = "")
    {
        if (links.Count == 0) return null;
        var hls = links.FirstOrDefault(l => l.Kind == HLS);
        if (hls != null) return hls;

        var mp4s = links.Where(l => l.Kind == MP4).ToList();
        if (mp4s.Count == 0) return links.LastOrDefault(l => l.Kind != AUDIO) ?? links[^1];

        // Link trọn bộ chỉ xuất hiện khi người dùng chuyển tới đúng thẻ đó -> tin nhất
        var prog = mp4s.Where(l => IsProgressive(l.Url)).ToList();
        if (prog.Count > 0) return prog.OrderByDescending(l => l.LastActivity).First();

        var pageIds = IdsInPageUrl(pageUrl);
        var ofPage = mp4s.Where(l => pageIds.Contains(VideoId(l.Url))).ToList();
        if (ofPage.Count > 0) return Sharpest(ofPage);

        var active = mp4s.OrderByDescending(l => l.LastActivity).First();
        var id = VideoId(active.Url);
        return Sharpest(id.Length > 0 ? mp4s.Where(l => VideoId(l.Url) == id).ToList() : [active]);
    }

    /// <summary>Bản nét nhất; bằng nhau thì bản hoạt động gần nhất.</summary>
    private static CapturedLink Sharpest(List<CapturedLink> same) =>
        same.OrderByDescending(NoteHeight).ThenByDescending(l => l.LastActivity).First();

    /// <summary>
    /// Tìm stream tiếng đi cặp với stream hình đã chọn (Facebook tách hình/tiếng).
    /// Có mã video thì bắt buộc cùng mã — trang có nhiều video (story, feed) mà ghép nhầm
    /// tiếng của video khác thì tệ hơn là không có tiếng. Không có mã thì lấy stream gần nhất.
    /// </summary>
    public static CapturedLink? PairedAudio(IReadOnlyList<CapturedLink> links, CapturedLink video)
    {
        // link trọn bộ đã có tiếng sẵn — ghép thêm là hỏng
        if (IsProgressive(video.Url)) return null;

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

namespace SimpleVidDownload.Services;

public enum Lang { Vi, En }

/// <summary>
/// Toàn bộ chữ hiển thị của app, hai thứ tiếng. Mỗi cửa sổ có hàm ApplyTexts() đọc từ đây,
/// gọi lại khi người dùng đổi ngôn ngữ.
/// </summary>
public static class Loc
{
    public static Lang Current { get; set; } = Lang.Vi;

    public static string T(string key) =>
        S.TryGetValue(key, out var v) ? (Current == Lang.Vi ? v[0] : v[1]) : key;

    private static readonly Dictionary<string, string[]> S = new()
    {
        // --- cửa sổ chính ---
        ["title"]        = ["DCDownload — dán link là xong", "DCDownload — paste a link, done"],
        ["appName"]      = ["DCDownload", "DCDownload"],
        ["tagline"]      = ["dán link là xong", "paste a link, done"],
        ["update"]       = ["Cập nhật engine", "Update engine"],
        ["updateTip"]    = ["Cập nhật yt-dlp lên bản mới nhất — bấm khi YouTube đột nhiên tải lỗi",
                            "Update yt-dlp to the latest build — press this when YouTube suddenly breaks"],

        ["lblLink"]      = ["Link video", "Video link"],
        ["urlHint"]      = ["Dán link video vào đây rồi bấm Enter...", "Paste a video link here and press Enter..."],
        ["paste"]        = ["Dán link", "Paste"],

        ["lblQuality"]   = ["Chất lượng", "Quality"],
        ["q0"]           = ["Tốt nhất — 4K nếu có", "Best — 4K if available"],
        ["q1"]           = ["Tối đa 1440p (2K)", "Up to 1440p (2K)"],
        ["q2"]           = ["Tối đa 1080p", "Up to 1080p"],
        ["q3"]           = ["Tối đa 720p", "Up to 720p"],
        ["q4"]           = ["Tối đa 480p", "Up to 480p"],
        ["q5"]           = ["Chỉ lấy âm thanh (MP3)", "Audio only (MP3)"],
        ["qualityTip"]   = ["4K chỉ tải được nếu video gốc có; không có thì tự lùi xuống mức cao nhất sẵn có.\nFile 4K rất nặng — một phim 10 phút có thể hơn 1 GB.",
                            "4K only works if the source has it; otherwise it falls back to the highest available.\n4K files are large — a 10-minute video can exceed 1 GB."],

        ["playlist"]     = ["Tải cả playlist", "Whole playlist"],
        ["playlistTip"]  = ["Dán link playlist và tải toàn bộ", "Paste a playlist link to grab all of it"],
        ["cookie"]       = ["Dùng cookie", "Use cookies"],
        ["cookieTip"]    = ["Cho video riêng tư hoặc giới hạn tuổi — nên chọn edge hoặc firefox",
                            "For private or age-restricted videos — prefer edge or firefox"],

        ["lblFolder"]    = ["Lưu vào", "Save to"],
        ["browse"]       = ["Chọn...", "Browse..."],
        ["openFolder"]   = ["Mở thư mục", "Open folder"],

        ["download"]     = ["⬇   TẢI XUỐNG", "⬇   DOWNLOAD"],
        ["cancel"]       = ["✖   HỦY", "✖   CANCEL"],
        ["capture"]      = ["🌐   Bắt video từ trang web", "🌐   Capture from a web page"],
        ["captureTip"]   = ["Mở trình duyệt nhúng để bắt link video — dùng khi dán link báo Unsupported URL",
                            "Opens an embedded browser to sniff the video link — use it when a link gives Unsupported URL"],

        ["openVideo"]    = ["▶  Mở video", "▶  Open video"],
        ["openVideoTip"] = ["Mở file vừa tải bằng trình phát mặc định",
                            "Open the file you just downloaded in your default player"],
        ["msgFileGone"]  = ["Không tìm thấy file nữa — có thể đã bị di chuyển hoặc xoá:\n{0}",
                            "The file is no longer there — it may have been moved or deleted:\n{0}"],

        ["logHeader"]    = ["NHẬT KÝ", "LOG"],
        ["logHint"]      = ["Tiến trình tải sẽ hiện ở đây.", "Download progress shows up here."],

        // --- trạng thái ---
        ["ready"]        = ["Sẵn sàng — dán link rồi bấm TẢI XUỐNG (hoặc Enter).",
                            "Ready — paste a link and hit DOWNLOAD (or press Enter)."],
        ["fetching"]     = ["Đang lấy thông tin video...", "Fetching video info..."],
        ["updating"]     = ["Đang cập nhật yt-dlp lên bản mới nhất...", "Updating yt-dlp to the latest build..."],
        ["merging"]      = ["Đang ghép / chuyển đổi file...", "Merging / converting the file..."],
        ["cancelling"]   = ["Đang hủy...", "Cancelling..."],
        ["cancelled"]    = ["Đã hủy tải.", "Download cancelled."],
        ["doneNamed"]    = ["✅ Xong! Đã lưu: ", "✅ Done! Saved: "],
        ["done"]         = ["✅ Xong! File đã lưu vào thư mục.", "✅ Done! The file is in your folder."],
        ["failed"]       = ["❌ Có lỗi — xem chi tiết bên dưới.", "❌ Something went wrong — details below."],
        ["cantRun"]      = ["❌ Không chạy được yt-dlp: ", "❌ Could not run yt-dlp: "],
        ["gotLink"]      = ["✅ Đã lấy link từ trình duyệt. Bấm TẢI XUỐNG.",
                            "✅ Got the link from the browser. Hit DOWNLOAD."],
        ["embedBusy"]    = ["Link nhúng — đang mở player để lấy link video thật...",
                            "Embed page — opening the player to get the real video link..."],
        ["embedOk"]      = ["✅ Đã lấy được link thật, bắt đầu tải...", "✅ Got the real link, starting the download..."],
        ["embedFail"]    = ["❌ Không lấy được link video thật từ trang nhúng.",
                            "❌ Could not get the real video link from that embed page."],

        // --- hộp thoại ---
        ["msgNeedUrl"]   = ["Hãy dán link video vào ô (bắt đầu bằng http/https).",
                            "Paste a video link into the box (it must start with http/https)."],
        ["msgEmbedFail"] = ["Không lấy được link video thật từ trang nhúng này.\n\nCách khác: bấm 🌐 Bắt video từ trang web, mở trang gốc, bấm play rồi chọn link [MP4] hoặc [HLS].",
                            "Could not get the real video link from this embed page.\n\nAlternative: press 🌐 Capture from a web page, open the original page, hit play, then pick the [MP4] or [HLS] link."],
        ["msgNoEngine"]  = ["Thiếu yt-dlp.exe trong:\n{0}\n\nChạy setup.ps1 để tải engine về.",
                            "yt-dlp.exe is missing from:\n{0}\n\nRun setup.ps1 to fetch the engines."],

        // --- cửa sổ bắt video ---
        ["capTitle"]     = ["Bắt video từ trang web", "Capture from a web page"],
        ["capInfo"]      = ["Mở trang video và chờ vài giây — link sẽ tự hiện bên dưới. Nếu chưa thấy thì bấm ▶ play một cái.",
                            "Open the video page and wait a few seconds — links show up below. If nothing appears, hit ▶ play once."],
        ["capCount"]     = ["{0} link bắt được", "{0} link(s) captured"],
        ["capBackTip"]   = ["Quay lại trang trước", "Go back"],
        ["capGo"]        = ["Đi", "Go"],
        ["capUse"]       = ["⬇  Dùng link này để TẢI", "⬇  Use this link"],
        ["capCopy"]      = ["Copy link", "Copy link"],
        ["msgNoLink"]    = ["Chưa bắt được link nào.\nHãy bấm ▶ play video trong trang rồi chờ vài giây.",
                            "No link captured yet.\nHit ▶ play on the page and wait a few seconds."],
        ["msgNoWebView"] = ["Không khởi động được trình duyệt nhúng (WebView2).\nMáy có thể chưa cài WebView2 Runtime.",
                            "Could not start the embedded browser (WebView2).\nThe WebView2 Runtime may not be installed."],

        // --- cửa sổ giải mã ---
        ["resTitle"]     = ["Đang lấy link video thật...", "Getting the real video link..."],
        ["resStatus"]    = ["Đang mở player để lấy link video thật. Chờ vài giây, cửa sổ này tự đóng...",
                            "Opening the player to get the real video link. A few seconds — this window closes itself..."],
        ["resRetry"]     = ["Vẫn đang thử... nếu thấy nút ▶ trong khung dưới, bạn bấm giúp một cái.",
                            "Still trying... if you see a ▶ play button below, please click it once."],
    };
}

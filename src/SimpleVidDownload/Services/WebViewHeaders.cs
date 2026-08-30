using Microsoft.Web.WebView2.Core;

namespace SimpleVidDownload.Services;

/// <summary>
/// Đọc header an toàn từ WebView2.
/// SDK này KHÔNG có TryGetHeader — chỉ có Contains() + GetHeader(), và GetHeader ném lỗi
/// nếu header không tồn tại, nên luôn phải hỏi Contains trước.
/// </summary>
public static class WebViewHeaders
{
    public static string Get(CoreWebView2HttpRequestHeaders? headers, string name)
    {
        try
        {
            if (headers != null && headers.Contains(name))
                return headers.GetHeader(name) ?? "";
        }
        catch { }
        return "";
    }

    public static string Get(CoreWebView2HttpResponseHeaders? headers, string name)
    {
        try
        {
            if (headers != null && headers.Contains(name))
                return headers.GetHeader(name) ?? "";
        }
        catch { }
        return "";
    }

    /// <summary>Bộ ba header cần gắn lại cho yt-dlp để máy chủ không chặn.</summary>
    public static (string Referer, string Cookie, string UserAgent) Triple(CoreWebView2HttpRequestHeaders? h)
        => (Get(h, "Referer"), Get(h, "Cookie"), Get(h, "User-Agent"));
}

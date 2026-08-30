using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using SimpleVidDownload.Services;

namespace SimpleVidDownload;

/// <summary>
/// Mở trang player nhúng (blogger.com/video.g...) trong nền, cho nó chạy rồi tóm link stream thật.
/// Cần thiết vì extractor blogger của yt-dlp đã hỏng: trang Blogger đời mới không còn nhúng sẵn
/// VIDEO_CONFIG, nên chỉ trình duyệt thật mới lấy được link.
/// Lưu ý: gọi play() thôi KHÔNG đủ — trang chỉ dựng thẻ video sau khi có tương tác, nên phải click.
/// </summary>
public partial class ResolveWindow : Window
{
    private readonly string _embedUrl;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTime _start = DateTime.Now;
    private int _step;

    /// <summary>Link stream thật lấy được (null nếu thất bại).</summary>
    public CapturedLink? Result { get; private set; }

    public ResolveWindow(string embedUrl)
    {
        InitializeComponent();
        _embedUrl = embedUrl;
        Title = Loc.T("resTitle");
        LblStatus.Text = Loc.T("resStatus");

        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Web.Dispose();
        };
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData);
            await Web.EnsureCoreWebView2Async(env);
        }
        catch
        {
            Close();
            return;
        }

        var core = Web.CoreWebView2;
        core.AddWebResourceRequestedFilter("*",
            CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += OnRequested;
        core.WebResourceResponseReceived += OnResponse;

        _timer.Tick += OnTick;
        _timer.Start();

        try { core.Navigate(_embedUrl); } catch { Close(); }
    }

    private void OnRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var url = e.Request.Uri;
        if (MediaSniffer.ShouldSkip(url)) return;
        var kind = MediaSniffer.KindFromUrl(url);
        if (kind is null || kind == MediaSniffer.EMBED) return;   // bỏ qua chính trang nhúng
        Take(kind, url, e.Request.Headers);
    }

    private void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var url = e.Request.Uri;
        if (MediaSniffer.ShouldSkip(url) || MediaSniffer.IsEmbedPage(url)) return;

        var ct = WebViewHeaders.Get(e.Response?.Headers, "Content-Type");
        var kind = MediaSniffer.KindFromContentType(ct);
        if (kind is null) return;
        Take(kind, url, e.Request.Headers);
    }

    private void Take(string kind, string url, CoreWebView2HttpRequestHeaders h)
    {
        if (Result != null) return;

        var (referer, cookie, ua) = WebViewHeaders.Triple(h);
        Result = new CapturedLink(kind, url, referer, cookie, ua);
        Dispatcher.Invoke(Close);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (Result != null) { _timer.Stop(); Close(); return; }

        var sec = (DateTime.Now - _start).TotalSeconds;

        if (sec >= 3 && _step == 0) { _step = 1; Play(); }
        else if (sec >= 6 && _step == 1) { _step = 2; ClickCenter(); }
        else if (sec >= 10 && _step == 2) { _step = 3; Play(); ClickCenter(); }
        else if (sec >= 15 && _step == 3) { _step = 4; ClickCenter(); }
        else if (sec >= 20 && _step == 4) { _step = 5; Play(); ClickCenter(); }

        if (sec >= 12 && sec < 13)
            LblStatus.Text = Loc.T("resRetry");

        if (sec >= 32) { _timer.Stop(); Close(); }
    }

    private void Play()
    {
        var js = "(function(){var v=document.querySelector('video');" +
                 "if(v){v.muted=true;v.play();return 'ok';}return 'no';})()";
        Cdp("Runtime.evaluate", new { expression = js });
    }

    private void ClickCenter()
    {
        int x = (int)(Web.ActualWidth / 2);
        int y = (int)(Web.ActualHeight / 2);
        if (x <= 0 || y <= 0) return;

        Cdp("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y });
        Cdp("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 });
        Cdp("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 });
    }

    private void Cdp(string method, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            _ = Web.CoreWebView2?.CallDevToolsProtocolMethodAsync(method, json);
        }
        catch { }
    }
}

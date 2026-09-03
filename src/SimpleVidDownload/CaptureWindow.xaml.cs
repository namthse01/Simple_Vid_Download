using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using SimpleVidDownload.Services;

namespace SimpleVidDownload;

/// <summary>
/// Trình duyệt nhúng nghe tầng mạng để chộp link video — đúng cơ chế Video DownloadHelper.
/// Điểm sống còn: phải dùng bộ lọc 3 tham số kèm RequestSourceKinds.All, nếu không sẽ KHÔNG
/// thấy request bên trong iframe khác domain (OOPIF) — mà đa số trang phim đều nhúng player
/// từ domain khác, nên bỏ sót là mất sạch.
/// </summary>
public partial class CaptureWindow : Window
{
    private readonly ObservableCollection<CapturedLink> _links = new();
    private readonly Dictionary<string, CapturedLink> _byUrl = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Link được xin khúc gần nhất — mang dấu ▶ trong danh sách.</summary>
    private CapturedLink? _active;
    /// <summary>Địa chỉ trang hiện tại, kể cả khi trang tự đổi địa chỉ không tải lại (story tự chuyển).</summary>
    private string _pageUrl = "";

    /// <summary>
    /// Mỗi giây hỏi trang xem thẻ video đang hiện dài bao nhiêu giây — cách duy nhất bám theo
    /// người dùng bấm chuyển thẻ trong story, vì thẻ kế đã được tải sẵn, phát từ bộ đệm,
    /// không sinh thêm request mạng nào.
    /// </summary>
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// Lúc người dùng vừa bấm chuột trong trang. Story TỰ CHẠY sang thẻ kế sau vài giây, nên
    /// chỉ thẻ hiện ra NGAY SAU cú bấm mới là thẻ người dùng thật sự muốn — thẻ tự nhảy đến
    /// sau đó thì bỏ qua, kẻo vừa ngắm đúng thẻ xong bấm tải lại ra thẻ khác.
    /// </summary>
    private DateTime _lastUserClickAt = DateTime.MinValue;
    private static readonly TimeSpan UserWindow = TimeSpan.FromSeconds(4);

    /// <summary>Thẻ vừa xuất hiện là do người dùng tự bấm chuyển (chứ không phải story tự chạy)?</summary>
    private bool ByUserNow => DateTime.Now - _lastUserClickAt <= UserWindow;

    /// <summary>Báo về cho app mỗi lần người dùng bấm trong trang. Dùng pointerdown + capture
    /// để nghe được cả khi trang tự chặn sự kiện ở tầng trên.</summary>
    private const string ClickReporter =
        "try{document.addEventListener('pointerdown',function(){" +
        "try{window.chrome.webview.postMessage('u');}catch(e){}},true);}catch(e){}";

    /// <summary>Người dùng đã tự bấm chọn một dòng chưa — nếu rồi thì đừng tự đổi lựa chọn của họ.</summary>
    private bool _userPicked;
    private readonly string _startUrl;

    private CoreWebView2Environment? _env;
    private bool _adOn = true;
    private int _adBlocked;
    private string? _adScriptId;

    /// <summary>Địa chỉ trang người dùng đang mở — không bao giờ chặn chính nó.</summary>
    private string _topUrl = "";

    /// <summary>Link người dùng đã chọn (null nếu đóng cửa sổ mà không chọn).</summary>
    public CapturedLink? Chosen { get; private set; }
    /// <summary>Stream tiếng đi cặp với link đã chọn — chỉ có khi trang tách hình/tiếng (Facebook).</summary>
    public CapturedLink? ChosenAudio { get; private set; }
    /// <summary>Tiêu đề trang lúc chọn — dùng đặt tên file.</summary>
    public string PageTitle { get; private set; } = "";

    public CaptureWindow(string startUrl)
    {
        InitializeComponent();
        _startUrl = startUrl.StartsWith("http") ? startUrl : "";
        LstLinks.ItemsSource = _links;
        LstLinks.PreviewMouseDown += (_, _) => _userPicked = true;
        TxtAddr.Text = string.IsNullOrEmpty(_startUrl) ? "https://" : _startUrl;

        Title = Loc.T("capTitle");
        LblInfo.Text = Loc.T("capInfo");
        BtnBack.ToolTip = Loc.T("capBackTip");
        BtnGo.Content = Loc.T("capGo");
        BtnUse.Content = Loc.T("capUse");
        BtnCopy.Content = Loc.T("capCopy");
        ChkAdBlock.Content = Loc.T("capAdBlock");
        ChkAdBlock.ToolTip = Loc.T("capAdBlockTip");

        Loaded += async (_, _) => await InitWebViewAsync();
        Closed += (_, _) =>
        {
            _watch.Stop();
            Web.Dispose();
        };
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData);
            _env = env;
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.T("msgNoWebView") + Environment.NewLine + Environment.NewLine + ex.Message,
                Loc.T("capTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        var core = Web.CoreWebView2;

        // 3 tham số = bắt được cả request bên trong iframe khác domain
        core.AddWebResourceRequestedFilter("*",
            CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);

        core.WebResourceRequested += OnResourceRequested;
        core.WebResourceResponseReceived += OnResponseReceived;
        core.SourceChanged += (_, _) =>
        {
            _pageUrl = core.Source;
            TxtAddr.Text = core.Source;
        };

        // Sang trang khac thi XOA danh sach cu.
        // Khong xoa thi link cua video truoc van nam dau danh sach va bi chon nham.
        core.NavigationStarting += (_, e) =>
        {
            _topUrl = e.Uri;              // nho lai de khong chan nham chinh trang nay
            _pageUrl = e.Uri;
            if (e.IsRedirected) return;   // chuyen huong cua cung trang thi giu nguyen
            Dispatcher.Invoke(ResetLinks);
        };

        // Popunder: bam vao dau cung bat cua so moi. Khong bao gio mo cua so moi ca —
        // nguoi dung that su bam thi mo ngay tai day cho khoi cut duong.
        core.NewWindowRequested += (_, e) =>
        {
            if (!_adOn) return;
            e.Handled = true;

            bool laQuangCao = AdBlock.ShouldBlock(e.Uri, isDocument: true, isTopLevel: false);
            if (!e.IsUserInitiated || laQuangCao) { Blocked(); return; }

            try { core.Navigate(e.Uri); } catch { }
        };

        try { _adScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(AdBlock.PageScript); }
        catch { }

        core.WebMessageReceived += (_, _) => _lastUserClickAt = DateTime.Now;
        try { await core.AddScriptToExecuteOnDocumentCreatedAsync(ClickReporter); } catch { }

        _watch.Tick += async (_, _) => await WatchPlayingAsync();
        _watch.Start();

        if (!string.IsNullOrEmpty(_startUrl))
        {
            try { core.Navigate(_startUrl); } catch { }
        }
    }

    /// <summary>Thẻ video to nhất đang trong khung nhìn (ưu tiên thẻ đang phát): trả về độ dài.</summary>
    private const string JsPlaying =
        "(function(){var best=null,bs=-1;var vs=document.querySelectorAll('video');" +
        "for(var i=0;i<vs.length;i++){var v=vs[i];var r=v.getBoundingClientRect();" +
        "if(r.width<80||r.height<80)continue;" +
        "if(r.bottom<=0||r.right<=0||r.top>=innerHeight||r.left>=innerWidth)continue;" +
        "var s=r.width*r.height+(v.paused?0:1e7);if(s>bs){bs=s;best=v;}}" +
        "if(!best||!best.duration||!isFinite(best.duration))return 'x';" +
        "return String(best.duration);})()";

    private async Task WatchPlayingAsync()
    {
        var core = Web?.CoreWebView2;
        if (core is null || _links.Count == 0) return;
        try
        {
            var raw = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new { expression = JsPlaying, returnByValue = true }));
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("result", out var r)
                || !r.TryGetProperty("value", out var v)
                || v.ValueKind != JsonValueKind.String) return;
            if (!double.TryParse(v.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds)) return;

            var hit = MediaSniffer.ByDuration(_links, seconds);
            if (hit is not null && (_active is null || ByUserNow)) SetChoice(hit);
        }
        catch { }
    }

    // nhận diện theo hình dạng URL
    private void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var url = e.Request.Uri;

        if (_adOn && _env is not null
            && AdBlock.ShouldBlock(url,
                   isDocument: e.ResourceContext == CoreWebView2WebResourceContext.Document,
                   isTopLevel: string.Equals(url, _topUrl, StringComparison.OrdinalIgnoreCase)))
        {
            // 204 = không có nội dung: trang chạy tiếp bình thường, chỉ là chẳng nhận được gì.
            e.Response = _env.CreateWebResourceResponse(null, 204, "No Content", "");
            Blocked();
            return;
        }

        // Facebook xin từng khúc byte của cùng một file: gom về link cả file, kẻo tải được mảnh rời
        var range = MediaSniffer.RangeLength(url);
        url = MediaSniffer.StripRange(url);
        if (MediaSniffer.ShouldSkip(url)) return;

        var kind = MediaSniffer.KindFromUrl(url);
        if (kind is null) return;

        Add(Make(kind, url, e.Request.Headers), range);
    }

    /// <summary>Chế độ mổ xẻ: có file dump.on cạnh app thì lưu thân HTML/JSON trang về logs\dump\ để soi cấu trúc.</summary>
    private static readonly bool DumpOn = File.Exists(Path.Combine(AppPaths.AppRoot, "dump.on"));
    private static int _dumpN;

    private async Task DumpAsync(CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            if (e.Response is null) return;
            var ct = WebViewHeaders.Get(e.Response.Headers, "Content-Type");
            if (!(ct.Contains("html") || ct.Contains("json") || ct.Contains("javascript"))) return;
            var uri = new Uri(e.Request.Uri);
            if (!uri.Host.EndsWith("facebook.com") || uri.Host.StartsWith("static")) return;
            if (uri.AbsolutePath.Contains("/rsrc.php")) return;   // script tĩnh, không có dữ liệu

            using var s = await e.Response.GetContentAsync();
            if (s is null) return;
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);

            var dir = Path.Combine(AppPaths.LogDir, "dump");
            Directory.CreateDirectory(dir);
            var n = Interlocked.Increment(ref _dumpN);
            var name = Regex.Replace(uri.AbsolutePath.Trim('/'), @"[^A-Za-z0-9_.-]+", "_");
            File.WriteAllBytes(Path.Combine(dir, $"{n:D3}_{name}.txt"), ms.ToArray());
            File.AppendAllText(Path.Combine(dir, "index.txt"), $"{n:D3}  {ms.Length,9}  {ct}  {e.Request.Uri}\n");
        }
        catch { }
    }

    // nhận diện theo Content-Type — bắt được cả link không có đuôi file
    private void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (DumpOn) _ = DumpAsync(e);

        var url = MediaSniffer.StripRange(e.Request.Uri);
        if (MediaSniffer.ShouldSkip(url)) return;

        var contentType = WebViewHeaders.Get(e.Response?.Headers, "Content-Type");
        var kind = MediaSniffer.KindFromContentType(contentType);
        if (kind is null) return;

        // request đã được đếm ở OnResourceRequested; ở đây chỉ để bắt link không có đuôi file
        Add(Make(kind, url, e.Request.Headers), 0, countActivity: false);
    }

    /// <summary>Dựng link kèm header của phiên; stream chỉ có tiếng thì gắn loại AUDIO cho khỏi chọn nhầm.</summary>
    private static CapturedLink Make(string kind, string url, CoreWebView2HttpRequestHeaders headers)
    {
        var (referer, cookie, ua) = WebViewHeaders.Triple(headers);
        if (kind == MediaSniffer.MP4 && MediaSniffer.LooksLikeAudio(url)) kind = MediaSniffer.AUDIO;
        return new CapturedLink(kind, url, referer, cookie, ua, MediaSniffer.QualityNote(url));
    }

    /// <param name="range">Độ dài khúc byte trang vừa xin (-1 = xin cả file, 0 = không biết).</param>
    /// <param name="countActivity">Có tính lần này là hoạt động không (mỗi request chỉ tính một lần).</param>
    private void Add(CapturedLink link, long range, bool countActivity = true)
    {
        Dispatcher.Invoke(() =>
        {
            bool substantial = countActivity && MediaSniffer.IsSubstantial(range);

            if (_byUrl.TryGetValue(link.Url, out var known))
            {
                if (!countActivity) return;
                known.Touch(Math.Max(0, range), substantial);
                link = known;
            }
            else
            {
                link.Touch(Math.Max(0, range), substantial);
                _byUrl[link.Url] = link;
                _links.Add(link);
                UpdateCounts();
            }

            // video đang phát là link được xin khúc gần nhất -> đánh dấu ▶ cho dễ nhận
            if (substantial && link.Kind != MediaSniffer.AUDIO)
            {
                if (_active is null || ByUserNow) SetChoice(link);
                return;
            }

            // Chưa có tín hiệu nào về video đang phát thì tạm đoán, để luôn có sẵn một lựa chọn.
            // Link dau thuong la trang player trung gian, stream that den sau.
            if (_active is null && !_userPicked)
                LstLinks.SelectedItem = MediaSniffer.PickBest(_links, _pageUrl);
        });
    }

    /// <summary>
    /// Chốt link đang phát: dấu ▶ và dòng được chọn LUÔN là một. Người dùng đã tự bấm chọn
    /// thì chỉ đổi dấu ▶, không giành lấy lựa chọn của họ.
    /// </summary>
    private void SetChoice(CapturedLink link)
    {
        if (!ReferenceEquals(_active, link))
        {
            _active?.SetActive(false);
            link.SetActive(true);
            _active = link;
        }
        if (!_userPicked) LstLinks.SelectedItem = link;
    }

    /// <summary>Xoá sạch link đã bắt (gọi khi chuyển sang trang khác).</summary>
    private void ResetLinks()
    {
        _links.Clear();
        _byUrl.Clear();
        _active = null;
        _userPicked = false;
        LstLinks.SelectedIndex = -1;
        _adBlocked = 0;
        UpdateCounts();
    }

    /// <summary>Đếm thêm một thứ vừa chặn được (gọi từ luồng nào cũng an toàn).</summary>
    private void Blocked()
    {
        _adBlocked++;
        if (Dispatcher.CheckAccess()) UpdateCounts();
        else Dispatcher.BeginInvoke(UpdateCounts);
    }

    private void UpdateCounts()
    {
        var s = _links.Count > 0 ? string.Format(Loc.T("capCount"), _links.Count) : "";
        if (_adOn && _adBlocked > 0)
            s = (s.Length > 0 ? s + "   ·   " : "") + string.Format(Loc.T("capAdsBlocked"), _adBlocked);
        LblCount.Text = s;
    }

    /// <summary>Tắt/bật chặn quảng cáo rồi tải lại trang cho thấy ngay khác biệt.</summary>
    private async void ChkAdBlock_Changed(object sender, RoutedEventArgs e)
    {
        _adOn = ChkAdBlock.IsChecked == true;

        // Ô tick nằm trước khung duyệt trong XAML nên sự kiện này bắn ngay lúc dựng giao diện,
        // lúc đó Web còn chưa tồn tại — phải hỏi null cả chính nó, không riêng CoreWebView2.
        var core = Web?.CoreWebView2;
        if (core is null) return;

        try
        {
            if (_adOn)
            {
                _adScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(AdBlock.PageScript);
            }
            else if (_adScriptId is not null)
            {
                core.RemoveScriptToExecuteOnDocumentCreated(_adScriptId);
                _adScriptId = null;
            }

            _adBlocked = 0;
            UpdateCounts();
            core.Reload();
        }
        catch { }
    }

    // ================= các nút =================

    private void Navigate()
    {
        var u = TxtAddr.Text.Trim();
        if (!u.StartsWith("http")) u = "https://" + u;
        try { Web.CoreWebView2?.Navigate(u); } catch { }
    }

    private void BtnGo_Click(object sender, RoutedEventArgs e) => Navigate();

    private void TxtAddr_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) Navigate();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack();
    }

    /// <summary>
    /// Người dùng tự bấm chọn thì tôn trọng; không thì lấy link đang phát (dấu ▶) —
    /// đó là thứ hai tín hiệu (hoạt động mạng + độ dài thẻ đang hiện) cùng chỉ vào.
    /// </summary>
    private CapturedLink? CurrentSelection =>
        _userPicked
            ? LstLinks.SelectedItem as CapturedLink
            : _active ?? LstLinks.SelectedItem as CapturedLink ?? MediaSniffer.PickBest(_links, _pageUrl);

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var link = CurrentSelection;
        if (link is null) return;
        try { Clipboard.SetText(link.Url); } catch { }
    }

    private void BtnUse_Click(object sender, RoutedEventArgs e)
    {
        var link = CurrentSelection;
        if (link is null)
        {
            MessageBox.Show(Loc.T("msgNoLink"), Loc.T("capTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Chosen = link;
        // hình và tiếng tách đôi (Facebook) -> đưa luôn stream tiếng để bên ngoài tải và ghép
        ChosenAudio = link.Kind == MediaSniffer.MP4 ? MediaSniffer.PairedAudio(_links, link) : null;
        try { PageTitle = Web.CoreWebView2?.DocumentTitle ?? ""; } catch { }
        WriteCaptureLog(link);
        Close();
    }

    /// <summary>
    /// Ghi lại trang, link chọn và toàn bộ link đã bắt (đầy đủ, không cắt). Lần sau có "tải nhầm
    /// video" thì còn dấu vết mà mổ — log của yt-dlp cắt cụt URL nên không dùng được.
    /// </summary>
    private void WriteCaptureLog(CapturedLink chosen)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("page:   " + _pageUrl);
            sb.AppendLine("title:  " + PageTitle);
            sb.AppendLine("click:  " + (_lastUserClickAt == DateTime.MinValue
                ? "-" : _lastUserClickAt.ToString("HH:mm:ss.fff")));
            sb.AppendLine("chosen: " + chosen.Url);
            sb.AppendLine("audio:  " + (ChosenAudio?.Url ?? "-"));
            sb.AppendLine("--- captured ---");
            foreach (var l in _links)
                sb.AppendLine($"{l.LastActivity:HH:mm:ss.fff} {(l.IsActive ? "▶" : " ")} [{l.Kind} {l.Note}] " +
                              $"id={MediaSniffer.VideoId(l.Url)} dur={MediaSniffer.DurationSeconds(l.Url)} " +
                              $"bytes={l.Bytes}  {l.Url}");
            File.WriteAllText(Path.Combine(AppPaths.LogDir, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.log"),
                sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }
}

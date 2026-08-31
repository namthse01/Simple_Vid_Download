using System.Collections.ObjectModel;
using System.Windows;
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
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Người dùng đã tự bấm chọn một dòng chưa — nếu rồi thì đừng tự đổi lựa chọn của họ.</summary>
    private bool _userPicked;
    private readonly string _startUrl;

    /// <summary>Link người dùng đã chọn (null nếu đóng cửa sổ mà không chọn).</summary>
    public CapturedLink? Chosen { get; private set; }
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

        Loaded += async (_, _) => await InitWebViewAsync();
        Closed += (_, _) => Web.Dispose();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData);
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
        core.SourceChanged += (_, _) => TxtAddr.Text = core.Source;

        // Sang trang khac thi XOA danh sach cu.
        // Khong xoa thi link cua video truoc van nam dau danh sach va bi chon nham.
        core.NavigationStarting += (_, e) =>
        {
            if (e.IsRedirected) return;   // chuyen huong cua cung trang thi giu nguyen
            Dispatcher.Invoke(ResetLinks);
        };

        if (!string.IsNullOrEmpty(_startUrl))
        {
            try { core.Navigate(_startUrl); } catch { }
        }
    }

    // nhận diện theo hình dạng URL
    private void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var url = e.Request.Uri;
        if (MediaSniffer.ShouldSkip(url)) return;

        var kind = MediaSniffer.KindFromUrl(url);
        if (kind is null) return;

        var (referer, cookie, ua) = WebViewHeaders.Triple(e.Request.Headers);
        Add(new CapturedLink(kind, url, referer, cookie, ua));
    }

    // nhận diện theo Content-Type — bắt được cả link không có đuôi file
    private void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var url = e.Request.Uri;
        if (MediaSniffer.ShouldSkip(url)) return;

        var contentType = WebViewHeaders.Get(e.Response?.Headers, "Content-Type");
        var kind = MediaSniffer.KindFromContentType(contentType);
        if (kind is null) return;

        var (referer, cookie, ua) = WebViewHeaders.Triple(e.Request.Headers);
        Add(new CapturedLink(kind, url, referer, cookie, ua));
    }

    private void Add(CapturedLink link)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_seen.Add(link.Url)) return;
            _links.Add(link);
            LblCount.Text = string.Format(Loc.T("capCount"), _links.Count);

            // Tu chon link TOT NHAT chu khong phai link dau tien: link dau thuong la trang
            // player trung gian, stream that den sau. Nguoi dung da tu bam thi ton trong.
            if (!_userPicked) LstLinks.SelectedItem = MediaSniffer.PickBest(_links);
        });
    }

    /// <summary>Xoá sạch link đã bắt (gọi khi chuyển sang trang khác).</summary>
    private void ResetLinks()
    {
        _links.Clear();
        _seen.Clear();
        _userPicked = false;
        LstLinks.SelectedIndex = -1;
        LblCount.Text = "";
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

    private CapturedLink? CurrentSelection =>
        LstLinks.SelectedItem as CapturedLink ?? MediaSniffer.PickBest(_links);

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
        try { PageTitle = Web.CoreWebView2?.DocumentTitle ?? ""; } catch { }
        Close();
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SimpleVidDownload.Services;

namespace SimpleVidDownload;

public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private YtDlpRunner? _runner;
    private readonly StringBuilder _log = new();

    /// <summary>Link vừa bắt từ trình duyệt nhúng, kèm header của phiên đó.</summary>
    private CapturedLink? _captured;
    /// <summary>Tiêu đề trang lúc bắt link — dùng làm tên file, vì link stream không mang tên.</summary>
    private string _capturedTitle = "";

    private static readonly SolidColorBrush GoBrush = new((Color)ColorConverter.ConvertFromString("#A6E3A1"));
    private static readonly SolidColorBrush BadBrush = new((Color)ColorConverter.ConvertFromString("#F38BA8"));

    public MainWindow()
    {
        InitializeComponent();

        _settings = Settings.Load();
        TxtFolder.Text = !string.IsNullOrEmpty(_settings.Folder) && Directory.Exists(_settings.Folder)
            ? _settings.Folder
            : Settings.DefaultFolder;
        if (_settings.Quality >= 0 && _settings.Quality < CboQuality.Items.Count)
            CboQuality.SelectedIndex = _settings.Quality;

        Loc.Current = _settings.Language == "en" ? Lang.En : Lang.Vi;
        ApplyTexts();

        Loaded += (_, _) =>
        {
            if (!AppPaths.EngineReady)
            {
                MessageBox.Show(
                    string.Format(Loc.T("msgNoEngine"), AppPaths.EngineDir),
                    Loc.T("appName"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            TryFillFromClipboard();
            TxtUrl.Focus();
        };

        Closing += (_, _) =>
        {
            SaveSettings();
            _runner?.Cancel();
        };
    }

    // ================= tiện ích =================

    private void SaveSettings()
    {
        _settings.Folder = TxtFolder.Text.Trim();
        _settings.Quality = CboQuality.SelectedIndex;
        _settings.Language = Loc.Current == Lang.En ? "en" : "vi";
        _settings.Save();
    }

    // ================= ngôn ngữ =================

    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag?.ToString();
        Loc.Current = tag == "en" ? Lang.En : Lang.Vi;
        ApplyTexts();
        SaveSettings();
    }

    /// <summary>Đổi toàn bộ chữ trên cửa sổ sang ngôn ngữ đang chọn.</summary>
    private void ApplyTexts()
    {
        Title = Loc.T("title");
        LblAppName.Text = Loc.T("appName");
        LblTagline.Text = Loc.T("tagline");

        BtnUpdate.Content = Loc.T("update");
        BtnUpdate.ToolTip = Loc.T("updateTip");

        LblLink.Text = Loc.T("lblLink");
        LblUrlHint.Text = Loc.T("urlHint");
        BtnPaste.Content = Loc.T("paste");

        LblQuality.Text = Loc.T("lblQuality");
        var keep = CboQuality.SelectedIndex;
        for (int i = 0; i < CboQuality.Items.Count; i++)
            ((ComboBoxItem)CboQuality.Items[i]!).Content = Loc.T("q" + i);
        CboQuality.SelectedIndex = keep;

        ChkPlaylist.Content = Loc.T("playlist");
        ChkPlaylist.ToolTip = Loc.T("playlistTip");
        ChkCookie.Content = Loc.T("cookie");
        ChkCookie.ToolTip = Loc.T("cookieTip");

        LblFolder.Text = Loc.T("lblFolder");
        BtnFolder.Content = Loc.T("browse");
        BtnOpen.Content = Loc.T("openFolder");

        if (_runner is not { IsRunning: true }) BtnDownload.Content = Loc.T("download");
        BtnCapture.Content = Loc.T("capture");
        BtnCapture.ToolTip = Loc.T("captureTip");

        LblLogHeader.Text = Loc.T("logHeader");
        LblLogHint.Text = Loc.T("logHint");

        if (!_statusDirty) LblStatus.Text = Loc.T("ready");

        // nút ngôn ngữ đang chọn thì tô sáng
        var on = (SolidColorBrush)FindResource("Accent");
        var off = (SolidColorBrush)FindResource("Line");
        var onInk = (SolidColorBrush)FindResource("Ink");
        var offText = (SolidColorBrush)FindResource("Text");
        bool vi = Loc.Current == Lang.Vi;
        BtnVi.Background = vi ? on : off;
        BtnVi.Foreground = vi ? onInk : offText;
        BtnEn.Background = vi ? off : on;
        BtnEn.Foreground = vi ? offText : onInk;
    }

    /// <summary>Đã có thông báo trạng thái riêng thì đừng ghi đè bằng câu "sẵn sàng".</summary>
    private bool _statusDirty;

    private void TryFillFromClipboard()
    {
        try
        {
            var clip = Clipboard.GetText()?.Trim();
            if (!string.IsNullOrEmpty(clip) && clip.StartsWith("http") && !clip.Contains(' '))
                TxtUrl.Text = clip;
        }
        catch { }
    }

    private void AppendLog(string line)
    {
        _log.AppendLine(line);
        if (_log.Length > 12000) _log.Remove(0, _log.Length - 12000);
        TxtLog.Text = _log.ToString();
        TxtLog.ScrollToEnd();
    }

    private string SelectedBrowser =>
        (CboBrowser.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "edge";

    private void SetBusy(bool busy)
    {
        BtnDownload.Content = busy ? Loc.T("cancel") : Loc.T("download");
        BtnDownload.Background = busy ? BadBrush : GoBrush;
        BtnCapture.IsEnabled = !busy;
        BtnUpdate.IsEnabled = !busy;
    }

    // ================= các nút =================

    private void BtnPaste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var t = Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(t)) TxtUrl.Text = t.Trim();
        }
        catch { }
    }

    private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) BtnDownload_Click(sender, e);
    }

    private void BtnFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Chọn thư mục lưu video" };
        if (Directory.Exists(TxtFolder.Text)) dlg.InitialDirectory = TxtFolder.Text;
        if (dlg.ShowDialog() == true)
        {
            TxtFolder.Text = dlg.FolderName;
            SaveSettings();
        }
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var f = TxtFolder.Text.Trim();
        if (Directory.Exists(f))
            Process.Start(new ProcessStartInfo("explorer.exe", f) { UseShellExecute = true });
    }

    private void BtnCapture_Click(object sender, RoutedEventArgs e)
    {
        var win = new CaptureWindow(TxtUrl.Text.Trim()) { Owner = this };
        win.ShowDialog();

        if (win.Chosen != null)
        {
            _captured = win.Chosen;
            _capturedTitle = MediaSniffer.SafeFileName(win.PageTitle);
            TxtUrl.Text = win.Chosen.Url;
            SetStatus(Loc.T("gotLink"));
        }
    }

    private void SetStatus(string text)
    {
        LblStatus.Text = text;
        _statusDirty = true;
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_runner is { IsRunning: true }) return;
        await RunAsync(["-U"], Loc.T("updating"));
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        // đang tải -> nút này là nút Hủy
        if (_runner is { IsRunning: true })
        {
            _runner.Cancel();
            SetStatus(Loc.T("cancelling"));
            return;
        }

        var url = TxtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
        {
            MessageBox.Show(Loc.T("msgNeedUrl"), Loc.T("appName"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Link nhúng (Blogger...) yt-dlp không đọc được -> mở player lấy link thật trước
        if (MediaSniffer.IsEmbedPage(url))
        {
            SetStatus(Loc.T("embedBusy"));
            var resolver = new ResolveWindow(url) { Owner = this };
            resolver.ShowDialog();

            if (resolver.Result is null)
            {
                SetStatus(Loc.T("embedFail"));
                MessageBox.Show(Loc.T("msgEmbedFail"), Loc.T("appName"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _captured = resolver.Result;
            if (string.IsNullOrEmpty(_capturedTitle))
                _capturedTitle = "video_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            url = _captured.Url;
            TxtUrl.Text = url;
            SetStatus(Loc.T("embedOk"));
        }

        var folder = TxtFolder.Text.Trim();
        if (string.IsNullOrEmpty(folder)) folder = Settings.DefaultFolder;
        Directory.CreateDirectory(folder);
        TxtFolder.Text = folder;
        SaveSettings();

        var titleForFile = (_captured != null && _captured.Url == url) ? _capturedTitle : "";
        var args = YtDlpRunner.BuildDownloadArgs(
            url, folder, CboQuality.SelectedIndex, ChkPlaylist.IsChecked == true,
            ChkCookie.IsChecked == true, SelectedBrowser, _captured, titleForFile);

        await RunAsync(args, Loc.T("fetching"));
    }

    // ================= chạy yt-dlp =================

    private async Task RunAsync(IEnumerable<string> args, string startStatus)
    {
        _log.Clear();
        TxtLog.Text = "";
        Pb.IsIndeterminate = true;
        Pb.Value = 0;
        SetStatus(startStatus);
        SetBusy(true);

        _runner = new YtDlpRunner();
        _runner.LineReceived += line => Dispatcher.Invoke(() => AppendLog(line));
        _runner.ProgressChanged += pct => Dispatcher.Invoke(() =>
        {
            Pb.IsIndeterminate = false;
            Pb.Value = pct;
        });
        _runner.StageChanged += s => Dispatcher.Invoke(() => SetStatus(s));

        YtDlpResult result;
        try
        {
            result = await _runner.RunAsync(args);
        }
        catch (Exception ex)
        {
            Pb.IsIndeterminate = false;
            SetStatus(Loc.T("cantRun") + ex.Message);
            SetBusy(false);
            return;
        }

        Pb.IsIndeterminate = false;
        SetBusy(false);

        if (result.Cancelled)
        {
            SetStatus(Loc.T("cancelled"));
        }
        else if (result.Success)
        {
            Pb.Value = 100;
            SetStatus(string.IsNullOrEmpty(result.SavedFile)
                ? Loc.T("done")
                : Loc.T("doneNamed") + result.SavedFile);
        }
        else
        {
            SetStatus(Loc.T("failed"));
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                AppendLog(Environment.NewLine + "--- LỖI ---" + Environment.NewLine + result.StdErr);
        }
    }
}

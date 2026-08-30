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

    /// <summary>File vừa tải xong — cho nút "Mở video". Rỗng nghĩa là chưa có gì để mở.</summary>
    private string _lastSavedPath = "";

    /// <summary>Cho phép hủy khâu nâng cấp AI (khâu này có thể chạy rất lâu).</summary>
    private CancellationTokenSource? _upscaleCts;

    /// <summary>Quyết định sau khi dò nguồn: có cần chạy AI sau khi tải xong không, và lên bao nhiêu.</summary>
    private bool _pendingUpscale;
    private int _pendingUpscaleTarget;

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
            _ = InitUpscaleAsync();
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
        _settings.Upscale = ChkUpscale.IsChecked == true;
        _settings.UpscaleTarget = CboUpscale.SelectedIndex;
        _settings.Save();
    }

    // ================= nâng cấp bằng AI =================

    /// <summary>
    /// Chỉ bật ô tick khi GPU thật sự chạy được. Cách chắc ăn nhất là nâng thử một ảnh 64x64:
    /// có Vulkan chưa chắc đã chạy nổi (driver cũ, GPU quá yếu).
    /// </summary>
    private async Task InitUpscaleAsync()
    {
        if (!Upscaler.ToolAvailable)
        {
            ChkUpscale.IsEnabled = false;
            CboUpscale.IsEnabled = false;
            ChkUpscale.ToolTip = Loc.T("upscaleNoTool");
            return;
        }

        ChkUpscale.ToolTip = Loc.T("upscaleCheck");
        bool ok = await Upscaler.ProbeGpuAsync();

        ChkUpscale.IsEnabled = ok;
        CboUpscale.IsEnabled = ok;
        ChkUpscale.ToolTip = ok ? Loc.T("upscaleTip") : Loc.T("upscaleNoGpu");
        CboUpscale.ToolTip = ChkUpscale.ToolTip;

        if (ok)
        {
            if (_settings.UpscaleTarget >= 0 && _settings.UpscaleTarget < CboUpscale.Items.Count)
                CboUpscale.SelectedIndex = _settings.UpscaleTarget;
            ChkUpscale.IsChecked = _settings.Upscale;
        }
    }

    private static string FormatEta(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:D2}"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} phút"
        : $"{Math.Max(1, (int)t.TotalSeconds)} giây";

    private async Task RunUpscaleAsync(string path, int target)
    {
        var info = await Upscaler.ProbeVideoAsync(path);
        if (info is not null && info.Height >= target)
        {
            SetStatus(Loc.T("upSkip"));
            return;
        }

        SetBusy(true);
        Pb.IsIndeterminate = false;
        Pb.Value = 0;
        _upscaleCts = new CancellationTokenSource();

        var reporter = new Progress<UpscaleProgress>(p =>
        {
            Pb.Value = p.Percent;
            SetStatus(p.Stage switch
            {
                "split" => Loc.T("upSplit"),
                "join" => Loc.T("upJoin"),
                _ => string.Format(Loc.T("upBusy"),
                        p.Percent.ToString("0"),
                        p.Eta.HasValue ? FormatEta(p.Eta.Value) : "?")
            });
        });

        string? outPath = null;
        try { outPath = await Upscaler.UpscaleAsync(path, target, reporter, _upscaleCts.Token); }
        catch { }

        bool cancelled = _upscaleCts.IsCancellationRequested;
        _upscaleCts.Dispose();
        _upscaleCts = null;
        SetBusy(false);

        if (outPath is not null && File.Exists(outPath))
        {
            Pb.Value = 100;
            _lastSavedPath = outPath;      // nút "Mở video" trỏ sang bản đã nâng
            BtnOpenVideo.IsEnabled = true;
            SetStatus(Loc.T("upDone") + Path.GetFileName(outPath));
        }
        else
        {
            SetStatus(cancelled ? Loc.T("cancelled") : Loc.T("upFail"));
        }
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
        CboQuality.ToolTip = Loc.T("qualityTip");
        var keep = CboQuality.SelectedIndex;
        for (int i = 0; i < CboQuality.Items.Count; i++)
            ((ComboBoxItem)CboQuality.Items[i]!).Content = Loc.T("q" + i);
        CboQuality.SelectedIndex = keep;

        ChkPlaylist.Content = Loc.T("playlist");
        ChkPlaylist.ToolTip = Loc.T("playlistTip");
        ChkCookie.Content = Loc.T("cookie");
        ChkCookie.ToolTip = Loc.T("cookieTip");

        LblUpscale.Text = Loc.T("lblUpscale");
        ChkUpscale.Content = Loc.T("upscale");
        var keepU = CboUpscale.SelectedIndex;
        for (int i = 0; i < CboUpscale.Items.Count; i++)
            ((ComboBoxItem)CboUpscale.Items[i]!).Content = Loc.T("u" + i);
        CboUpscale.SelectedIndex = keepU;
        // tooltip do InitUpscaleAsync đặt (tùy GPU có chạy được không), chỉ dịch lại khi đã biết
        if (Upscaler.GpuOk.HasValue)
        {
            ChkUpscale.ToolTip = Upscaler.GpuOk.Value ? Loc.T("upscaleTip") : Loc.T("upscaleNoGpu");
            CboUpscale.ToolTip = ChkUpscale.ToolTip;
        }

        LblFolder.Text = Loc.T("lblFolder");
        BtnFolder.Content = Loc.T("browse");
        BtnOpen.Content = Loc.T("openFolder");

        if (_runner is not { IsRunning: true }) BtnDownload.Content = Loc.T("download");
        BtnCapture.Content = Loc.T("capture");
        BtnCapture.ToolTip = Loc.T("captureTip");

        BtnOpenVideo.Content = Loc.T("openVideo");
        BtnOpenVideo.ToolTip = Loc.T("openVideoTip");

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

    private void BtnOpenVideo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastSavedPath)) return;

        if (!File.Exists(_lastSavedPath))
        {
            BtnOpenVideo.IsEnabled = false;
            MessageBox.Show(string.Format(Loc.T("msgFileGone"), _lastSavedPath),
                Loc.T("appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // UseShellExecute = mở bằng trình phát mặc định của máy
        Process.Start(new ProcessStartInfo(_lastSavedPath) { UseShellExecute = true });
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
        // đang nâng cấp AI -> nút này là nút Hủy
        if (_upscaleCts is { IsCancellationRequested: false })
        {
            _upscaleCts.Cancel();
            SetStatus(Loc.T("cancelling"));
            return;
        }

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

        // Bật nâng cấp AI thì dò nguồn TRƯỚC: nếu nguồn vốn đã có sẵn độ phân giải đích thì
        // tải thẳng bản gốc — vừa nhanh vừa nét thật, hơn hẳn bản AI dựng lại.
        _pendingUpscale = false;
        _pendingUpscaleTarget = 0;
        int? overrideHeight = null;

        bool wantUpscale = ChkUpscale.IsEnabled && ChkUpscale.IsChecked == true
                           && CboQuality.SelectedIndex != 5;   // chế độ MP3 thì không nâng gì cả
        if (wantUpscale)
        {
            int[] targets = [1080, 1440, 2160];
            int target = targets[Math.Clamp(CboUpscale.SelectedIndex, 0, targets.Length - 1)];

            SetStatus(Loc.T("probing"));
            BtnDownload.IsEnabled = false;
            int maxH = await YtDlpRunner.ProbeMaxHeightAsync(
                url, ChkCookie.IsChecked == true, SelectedBrowser, _captured);
            BtnDownload.IsEnabled = true;

            if (maxH >= target)
            {
                overrideHeight = target;                       // có sẵn -> lấy thẳng
                SetStatus(string.Format(Loc.T("srcHasIt"), target));
            }
            else
            {
                // không đủ (hoặc không dò được) -> tải bản tốt nhất rồi nâng
                _pendingUpscale = true;
                _pendingUpscaleTarget = target;
                if (maxH > 0) SetStatus(string.Format(Loc.T("srcNeedsAi"), maxH, target));
            }
        }

        var titleForFile = (_captured != null && _captured.Url == url) ? _capturedTitle : "";
        var args = YtDlpRunner.BuildDownloadArgs(
            url, folder, CboQuality.SelectedIndex, ChkPlaylist.IsChecked == true,
            ChkCookie.IsChecked == true, SelectedBrowser, _captured, titleForFile, overrideHeight);

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

        // lần tải mới -> file cũ không còn liên quan nữa
        _lastSavedPath = "";
        BtnOpenVideo.IsEnabled = false;

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

            // chỉ bật nút khi thật sự có file trên đĩa (lệnh -U chẳng hạn thì không có)
            if (!string.IsNullOrEmpty(result.SavedPath) && File.Exists(result.SavedPath))
            {
                _lastSavedPath = result.SavedPath;
                BtnOpenVideo.IsEnabled = true;

                // chỉ chạy AI khi bước dò nguồn đã kết luận là cần
                if (_pendingUpscale)
                {
                    _pendingUpscale = false;
                    await RunUpscaleAsync(result.SavedPath, _pendingUpscaleTarget);
                }
            }
        }
        else
        {
            SetStatus(Loc.T("failed"));
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                AppendLog(Environment.NewLine + "--- LỖI ---" + Environment.NewLine + result.StdErr);
        }
    }
}

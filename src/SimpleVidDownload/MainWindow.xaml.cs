using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
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

        Loaded += (_, _) =>
        {
            if (!AppPaths.EngineReady)
            {
                MessageBox.Show(
                    $"Thiếu yt-dlp.exe trong:{Environment.NewLine}{AppPaths.EngineDir}" +
                    $"{Environment.NewLine}{Environment.NewLine}Chạy setup.ps1 để tải engine về.",
                    "Tải Video", MessageBoxButton.OK, MessageBoxImage.Error);
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
        _settings.Save();
    }

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
        (CboBrowser.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "edge";

    private void SetBusy(bool busy)
    {
        BtnDownload.Content = busy ? "✖   HỦY" : "⬇   TẢI XUỐNG";
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
            LblStatus.Text = "✅ Đã lấy link từ trình duyệt. Bấm TẢI XUỐNG.";
        }
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_runner is { IsRunning: true }) return;
        await RunAsync(["-U"], "Đang cập nhật yt-dlp lên bản mới nhất...");
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        // đang tải -> nút này là nút Hủy
        if (_runner is { IsRunning: true })
        {
            _runner.Cancel();
            LblStatus.Text = "Đang hủy...";
            return;
        }

        var url = TxtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
        {
            MessageBox.Show("Hãy dán link video vào ô (bắt đầu bằng http/https).",
                "Tải Video", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Link nhúng (Blogger...) yt-dlp không đọc được -> mở player lấy link thật trước
        if (MediaSniffer.IsEmbedPage(url))
        {
            LblStatus.Text = "Link nhúng — đang mở player để lấy link video thật...";
            var resolver = new ResolveWindow(url) { Owner = this };
            resolver.ShowDialog();

            if (resolver.Result is null)
            {
                LblStatus.Text = "❌ Không lấy được link video thật từ trang nhúng.";
                MessageBox.Show(
                    "Không lấy được link video thật từ trang nhúng này." + Environment.NewLine + Environment.NewLine +
                    "Cách khác: bấm 🌐 Bắt video từ trang web, mở trang gốc, bấm play rồi chọn link [MP4] hoặc [HLS].",
                    "Tải Video", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _captured = resolver.Result;
            if (string.IsNullOrEmpty(_capturedTitle))
                _capturedTitle = "video_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            url = _captured.Url;
            TxtUrl.Text = url;
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

        await RunAsync(args, "Đang lấy thông tin video...");
    }

    // ================= chạy yt-dlp =================

    private async Task RunAsync(IEnumerable<string> args, string startStatus)
    {
        _log.Clear();
        TxtLog.Text = "";
        Pb.IsIndeterminate = true;
        Pb.Value = 0;
        LblStatus.Text = startStatus;
        SetBusy(true);

        _runner = new YtDlpRunner();
        _runner.LineReceived += line => Dispatcher.Invoke(() => AppendLog(line));
        _runner.ProgressChanged += pct => Dispatcher.Invoke(() =>
        {
            Pb.IsIndeterminate = false;
            Pb.Value = pct;
        });
        _runner.StageChanged += s => Dispatcher.Invoke(() => LblStatus.Text = s);

        YtDlpResult result;
        try
        {
            result = await _runner.RunAsync(args);
        }
        catch (Exception ex)
        {
            Pb.IsIndeterminate = false;
            LblStatus.Text = "❌ Không chạy được yt-dlp: " + ex.Message;
            SetBusy(false);
            return;
        }

        Pb.IsIndeterminate = false;
        SetBusy(false);

        if (result.Cancelled)
        {
            LblStatus.Text = "Đã hủy tải.";
        }
        else if (result.Success)
        {
            Pb.Value = 100;
            LblStatus.Text = string.IsNullOrEmpty(result.SavedFile)
                ? "✅ Xong! File đã lưu vào thư mục."
                : "✅ Xong! Đã lưu: " + result.SavedFile;
        }
        else
        {
            LblStatus.Text = "❌ Có lỗi — xem chi tiết bên dưới.";
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                AppendLog(Environment.NewLine + "--- LỖI ---" + Environment.NewLine + result.StdErr);
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DCDownloadSetup;

public partial class MainWindow : Window
{
    private bool _uninstallMode;
    private bool _done;
    private string _installedExe = "";

    public MainWindow()
    {
        InitializeComponent();

        _uninstallMode = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)
                   || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

        if (_uninstallMode) SetUpUninstallView();
        else TxtPath.Text = Installer.DefaultDir;

        LblFoot.Text = "Phiên bản " + Installer.Version;
    }

    private void SetUpUninstallView()
    {
        Title = "Gỡ cài đặt DCDownload";
        LblSub.Text = "Gỡ DCDownload khỏi máy";
        PanelOptions.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Visible;
        Pb.Visibility = Visibility.Collapsed;

        var dir = Installer.InstalledDir();
        LblStatus.Text = dir is null
            ? "Không tìm thấy bản cài nào trên máy."
            : "Sẽ xoá toàn bộ thư mục sau, gồm cả engine đã tải:";
        LblHint.Text = dir ?? "";

        BtnGo.Content = "Gỡ cài đặt";
        BtnGo.Background = (System.Windows.Media.Brush)FindResource("Bad");
        BtnGo.IsEnabled = dir is not null;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Chọn thư mục cài đặt" };
        if (Directory.Exists(TxtPath.Text)) dlg.InitialDirectory = TxtPath.Text;
        if (dlg.ShowDialog() == true)
            TxtPath.Text = Path.Combine(dlg.FolderName, Installer.AppName);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void BtnGo_Click(object sender, RoutedEventArgs e)
    {
        // đã cài xong -> nút này mở app
        if (_done)
        {
            if (File.Exists(_installedExe))
                Process.Start(new ProcessStartInfo(_installedExe) { UseShellExecute = true });
            Close();
            return;
        }

        if (_uninstallMode)
        {
            var ok = MessageBox.Show(
                "Xoá DCDownload khỏi máy?" + Environment.NewLine +
                "Video bạn đã tải về nằm ở thư mục khác nên không bị đụng tới.",
                "Gỡ cài đặt", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ok != MessageBoxResult.Yes) return;

            Installer.Uninstall();
            MessageBox.Show("Đã gỡ xong.", "DCDownload",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
            return;
        }

        var target = TxtPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show("Hãy chọn thư mục cài đặt.", "DCDownload",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PanelOptions.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Visible;
        BtnGo.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        LblHint.Text = "Lần đầu cài sẽ tải khoảng 250 MB, tuỳ mạng có thể mất vài phút.";

        var reporter = new Progress<Step>(s =>
        {
            LblStatus.Text = s.Text;
            Pb.Value = s.Percent;
        });

        try
        {
            await Installer.InstallAsync(target, ChkDesktop.IsChecked == true,
                ChkAi.IsChecked == true, reporter, CancellationToken.None);

            _done = true;
            _installedExe = Path.Combine(target, "DCDownload.exe");
            LblStatus.Text = "Cài đặt xong!";
            LblHint.Text = "Đã tạo shortcut trong Start Menu"
                + (ChkDesktop.IsChecked == true ? " và ngoài Desktop." : ".");
            Pb.Value = 100;
            BtnGo.Content = "Mở DCDownload";
            BtnGo.IsEnabled = true;
            BtnCancel.Content = "Đóng";
            BtnCancel.IsEnabled = true;
        }
        catch (Exception ex)
        {
            LblStatus.Text = "Cài đặt thất bại.";
            LblHint.Text = ex.Message;
            BtnCancel.IsEnabled = true;
            BtnCancel.Content = "Đóng";
        }
    }
}

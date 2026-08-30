using System.Windows;

namespace SimpleVidDownload;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Cho phép video tự chạy trong trình duyệt nhúng -> đỡ phải bấm play (né quảng cáo).
        // Phải đặt TRƯỚC khi tạo WebView2 đầu tiên thì mới có tác dụng.
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
            "--autoplay-policy=no-user-gesture-required");

        base.OnStartup(e);
    }
}

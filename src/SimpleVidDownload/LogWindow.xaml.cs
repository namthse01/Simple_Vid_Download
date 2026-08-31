using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using SimpleVidDownload.Services;

namespace SimpleVidDownload;

/// <summary>
/// Cửa sổ xem nhật ký đầy đủ. Cửa sổ chính chỉ chừa vài dòng cho gọn,
/// ai cần soi lỗi cho rõ thì mở cái này.
/// </summary>
public partial class LogWindow : Window
{
    private readonly string _text;

    public LogWindow(string logText)
    {
        InitializeComponent();

        _text = string.IsNullOrWhiteSpace(logText) ? Loc.T("logEmpty") : logText;
        TxtFull.Text = _text;

        Title = Loc.T("logWinTitle");
        BtnCopy.Content = Loc.T("logCopy");
        BtnSave.Content = Loc.T("logSave");
        BtnClose.Content = Loc.T("logClose");

        Loaded += (_, _) =>
        {
            TxtFull.CaretIndex = TxtFull.Text.Length;
            TxtFull.ScrollToEnd();
        };
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_text);
            LblNote.Text = Loc.T("logCopied");
        }
        catch { }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = "DCDownload-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
            Filter = "Text (*.txt)|*.txt|All files (*.*)|*.*",
            InitialDirectory = AppPaths.LogDir
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, _text, new UTF8Encoding(true));
            LblNote.Text = Loc.T("logSavedTo") + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            LblNote.Text = ex.Message;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}

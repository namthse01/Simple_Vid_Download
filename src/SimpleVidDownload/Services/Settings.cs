using System.IO;
using System.Text.Json;

namespace SimpleVidDownload.Services;

/// <summary>Nhớ thư mục lưu và chất lượng đã chọn giữa các lần mở app.</summary>
public class Settings
{
    public string? Folder { get; set; }
    public int Quality { get; set; }
    /// <summary>"vi" hoặc "en"</summary>
    public string Language { get; set; } = "vi";

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var s = JsonSerializer.Deserialize<Settings>(json);
                if (s != null) return s;
            }
        }
        catch { /* hỏng file thì dùng mặc định, không cần báo */ }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch { }
    }

    public static string DefaultFolder =>
        Directory.Exists(@"D:\vid")
            ? @"D:\vid"
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
}

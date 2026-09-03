using System.IO;
using System.Text;

namespace SimpleVidDownload.Services;

/// <summary>
/// Soi vài byte đầu file vừa tải để chắc là video hoàn chỉnh.
/// Bài học từ Facebook: trình duyệt tải video theo từng khúc byte; chộp đúng link khúc đó thì
/// yt-dlp vẫn báo thành công (mã thoát 0) nhưng file chỉ là một mảnh giữa — không player nào mở.
/// </summary>
public static class MediaFile
{
    /// <summary>
    /// MP4 tử tế mở đầu bằng hộp "ftyp" (file QuickTime cũ có thể là "moov"/"mdat"/"free").
    /// Mở đầu bằng "moof"/"styp"/"sidx" là mảnh DASH rời — chắc chắn không phát được.
    /// </summary>
    public static bool IsBareFragment(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[8];
            if (fs.Read(head) < 8) return false;
            var box = Encoding.ASCII.GetString(head[4..8]);
            return box is "moof" or "styp" or "sidx";
        }
        catch { return false; }
    }
}

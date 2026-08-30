# Tải Video — dán link là xong

Phần mềm tải video cho Windows, tương đương extension **Video DownloadHelper** nhưng mạnh hơn:
dán link → chọn chất lượng → bấm **TẢI XUỐNG**. Không cần cài gì thêm.

Engine bên dưới là **yt-dlp** (mã nguồn mở, hỗ trợ **hơn 1000 trang web**: YouTube, Facebook,
TikTok, X/Twitter, Instagram, Twitch, Bilibili, SoundCloud, Vimeo, Dailymotion...) và **ffmpeg**
để ghép video + âm thanh chất lượng cao.

## Cài đặt (chỉ làm 1 lần sau khi clone)

Thư mục `bin\` chứa engine (~390 MB) nên **không** được đưa lên git. Chạy script này để tải về:

```powershell
powershell -ExecutionPolicy Bypass -File setup.ps1
```

Script tự tải yt-dlp, ffmpeg + ffprobe, deno và WebView2 SDK từ trang phát hành chính thức,
rồi kiểm tra máy đã có WebView2 Runtime chưa (Windows 11 thường có sẵn theo Edge).

Yêu cầu: Windows 10/11, PowerShell 5.1 (có sẵn). Không cần cài Python hay gì thêm.

## Cách chạy

- Nháy đúp **`TaiVideo.bat`** (hoặc shortcut **Tai Video** trên Desktop).
- Nếu clipboard đang có sẵn link thì app tự điền luôn vào ô.

## Các tính năng

| Tính năng | Ghi chú |
|---|---|
| Dán link → tải | Nút **Dán link** hoặc Ctrl+V vào ô, bấm Enter là tải luôn |
| Chọn chất lượng | Tốt nhất / 1080p / 720p / 480p / **chỉ lấy MP3** |
| Tải cả playlist | Tick **Tải cả playlist** rồi dán link playlist |
| Video cần đăng nhập | Tick **Dùng cookie trình duyệt** (video riêng tư, giới hạn tuổi...) — nên chọn `edge` hoặc `firefox`, Chrome bản mới hay khóa cookie |
| Thanh tiến độ + log | Xem % tải, tốc độ, lỗi ngay trong app |
| Hủy giữa chừng | Nút TẢI XUỐNG biến thành nút **HỦY** khi đang tải |
| Cập nhật engine | Nút **Cập nhật engine** = cập nhật yt-dlp bản mới nhất (nên bấm khi YouTube đổi giao diện làm tải lỗi) |
| **🌐 Bắt video từ trang web** | Chế độ "y như Video DownloadHelper" — xem mục dưới |

## 🌐 Bắt video từ trang web (khi dán link báo `Unsupported URL`)

Dùng khi trang web không nằm trong danh sách yt-dlp hỗ trợ (trang nhỏ, phim lẻ, trang giấu
video trong iframe...).

1. Bấm nút **🌐 Bắt video từ trang web** → mở ra trình duyệt nhúng (engine Edge thật).
2. Vào trang video → **bấm ▶ play** như xem bình thường, chờ 2-3 giây.
3. Link video hiện dần ở danh sách dưới, có nhãn phân loại:
   - `[HLS]` — stream cắt nhỏ (.m3u8), chất lượng tốt nhất, app tự ghép lại thành MP4
   - `[MP4]` — file video trực tiếp
   - `[NHUNG]` — trang player trung gian (Blogger...). **Chọn được bình thường**: app sẽ tự mở
     player đó trong nền để lấy link thật, xem mục dưới.
4. Chọn 1 link → bấm **⬇ Dùng link này để TẢI** → quay về cửa sổ chính → bấm **TẢI XUỐNG**.

### Tự giải mã link nhúng — đỡ phải bấm play giữa rừng quảng cáo

Nhiều trang phim nhét video qua một player trung gian (hay gặp nhất là `blogger.com/video.g`).
Đưa thẳng link đó cho yt-dlp sẽ **báo lỗi** `Unable to extract JSON data`, vì trang Blogger đời mới
không còn nhúng sẵn thông tin video nữa.

App xử lý tự động: hễ link cần tải là link nhúng, app mở player đó trong một cửa sổ nhỏ, tự phát,
tóm lấy link `googlevideo` thật rồi mới giao cho yt-dlp. Mất khoảng **5 giây**, cửa sổ tự đóng.

Nhờ vậy bạn **không cần bấm play trên trang gốc** (nơi lắm quảng cáo, dễ bấm nhầm): chỉ cần mở trang,
đợi link `[NHUNG]` hiện ra rồi bấm tải. Trang player trung gian không có quảng cáo nên app tự bấm
trong đó là an toàn.

Nếu quá 30 giây chưa lấy được, cửa sổ vẫn hiện player để **bạn tự bấm ▶ một cái** rồi nó nhận ngay.

App cũng tự giải mã khi bạn dán thẳng link `blogger.com/video.g` vào ô địa chỉ.

Nút **Copy** để lấy link thô nếu muốn dùng chỗ khác.

### Vì sao cách này tải được còn dán link thường thì không?

- **Dán link thường**: yt-dlp tải HTML thô rồi tự mổ xẻ. Nó có ~1.800 bộ giải mã viết riêng cho
  từng trang. Trang nào không có sẵn bộ giải mã, mà video lại giấu sau JavaScript/iframe, thì chịu
  → báo `Unsupported URL`.
- **Bắt video từ trang web**: trình duyệt thật chạy hết JavaScript, giải mã, dựng player — vì trang
  *phải* đưa video ra thì mới phát được. App chỉ ngồi nghe tầng mạng, thấy file video chạy qua là
  chộp link, kèm luôn **cookie + referer + user-agent** của chính phiên duyệt đó rồi đưa cho yt-dlp
  tải. Máy chủ thấy y hệt một trình duyệt đang xem phim nên không chặn.

Đây đúng là cơ chế của Video DownloadHelper. Khác biệt: app bắt được cả video nằm trong **iframe
khác domain** (nhiều trang phim dùng player nhúng từ domain khác), và nhận diện cả link **không có
đuôi `.mp4`/`.m3u8`** bằng cách đọc `Content-Type` của phản hồi.

Tên file sẽ lấy theo **tiêu đề trang** (vì link stream trực tiếp không mang tên video).

Thư mục lưu mặc định: `D:\vid` (đổi được, app tự nhớ lựa chọn trong `settings.json`).

## Về tên file tiếng Việt

Tên file lưu trên đĩa **luôn đủ dấu tiếng Việt**. Nếu khung log trong app hiện chữ mất dấu thì đó chỉ là
hiển thị, file vẫn đúng — dòng `✅ Xong! Đã lưu: <tên file>` ở thanh trạng thái mới là tên thật.

(Kỹ thuật: yt-dlp mặc định ghi log theo bảng mã ANSI của Windows nên rụng dấu; app truyền thêm cờ
`--encoding utf-8` để log ra UTF-8. Các cách quen thuộc như `PYTHONUTF8=1`, `PYTHONIOENCODING=utf-8`
hay `chcp 65001` đều **không** có tác dụng với bản yt-dlp.exe đóng gói.)

## Khi gặp lỗi

1. **Tải YouTube lỗi đột ngột** → bấm **Cập nhật engine** rồi thử lại (YouTube đổi API liên tục, yt-dlp cập nhật vài ngày một lần).
2. **"Sign in to confirm you're not a bot" / video riêng tư** → tick **Dùng cookie trình duyệt**, chọn trình duyệt bạn đang đăng nhập.
3. **Trang không hỗ trợ** → danh sách trang hỗ trợ: <https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md>
4. Video DRM (Netflix, Disney+...) thì **không tải được** — đây là giới hạn chung, Video DownloadHelper cũng vậy.

## Cấu trúc thư mục

```
TaiVideo\
├─ TaiVideo.bat      ← nháy đúp để chạy
├─ TaiVideo.ps1      ← giao diện (PowerShell WPF)
├─ bin\
│  ├─ yt-dlp.exe     ← engine tải (github.com/yt-dlp/yt-dlp)
│  ├─ ffmpeg.exe     ← ghép video/audio, convert MP3
│  └─ ffprobe.exe
├─ logs\             ← log mỗi lần tải
└─ settings.json     ← nhớ thư mục lưu + chất lượng đã chọn
```

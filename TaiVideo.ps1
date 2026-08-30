# =============================================================
#  Tải Video — dán link là tải (engine: yt-dlp + ffmpeg)
#  Chạy bằng: TaiVideo.bat  hoặc shortcut "Tai Video" trên Desktop
# =============================================================

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin    = Join-Path $root 'bin'
$ytdlp  = Join-Path $bin 'yt-dlp.exe'
$logDir = Join-Path $root 'logs'
$cfg    = Join-Path $root 'settings.json'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

# yt-dlp (Python) xuất log UTF-8 để tên video tiếng Việt/Nhật không bị lỗi font
$env:PYTHONUTF8 = '1'
# cho yt-dlp thấy deno.exe + ffmpeg.exe trong bin (deno cần cho YouTube chất lượng cao)
$env:PATH = "$bin;$env:PATH"
# cho phép video tự chạy trong trình duyệt nhúng -> đỡ phải bấm play (né quảng cáo)
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--autoplay-policy=no-user-gesture-required'

if (-not (Test-Path $ytdlp)) {
    [System.Windows.MessageBox]::Show("Thiếu file bin\yt-dlp.exe.`nHãy tải lại từ: github.com/yt-dlp/yt-dlp/releases", 'Tải Video', 'OK', 'Error') | Out-Null
    exit 1
}

[xml]$xaml = @'
<Window
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Tải Video — dán link là xong" Height="620" Width="800" MinHeight="480" MinWidth="660"
    WindowStartupLocation="CenterScreen" Background="#1E1E2E">
  <Window.Resources>
    <Style TargetType="Button">
      <Setter Property="Background" Value="#89B4FA"/>
      <Setter Property="Foreground" Value="#11111B"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="FontSize" Value="14"/>
      <Setter Property="Padding" Value="14,7"/>
      <Setter Property="Margin" Value="6,0,0,0"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Cursor" Value="Hand"/>
    </Style>
    <Style TargetType="TextBox">
      <Setter Property="Background" Value="#313244"/>
      <Setter Property="Foreground" Value="#CDD6F4"/>
      <Setter Property="CaretBrush" Value="#CDD6F4"/>
      <Setter Property="BorderBrush" Value="#45475A"/>
      <Setter Property="Padding" Value="8,6"/>
      <Setter Property="FontSize" Value="14"/>
    </Style>
    <Style TargetType="TextBlock">
      <Setter Property="Foreground" Value="#CDD6F4"/>
      <Setter Property="FontSize" Value="14"/>
      <Setter Property="VerticalAlignment" Value="Center"/>
    </Style>
    <Style TargetType="CheckBox">
      <Setter Property="Foreground" Value="#CDD6F4"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="VerticalAlignment" Value="Center"/>
      <Setter Property="Margin" Value="16,0,0,0"/>
    </Style>
  </Window.Resources>
  <Grid Margin="18">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <Grid Grid.Row="0" Margin="0,0,0,10">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBlock Grid.Column="0" Text="Link video:" Margin="0,0,10,0" FontWeight="Bold"/>
      <TextBox x:Name="txtUrl" Grid.Column="1"/>
      <Button x:Name="btnPaste" Grid.Column="2" Content="Dán link"/>
    </Grid>

    <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,10">
      <TextBlock Text="Chất lượng:" Margin="0,0,8,0"/>
      <ComboBox x:Name="cboQuality" Width="200" FontSize="13" SelectedIndex="0" VerticalAlignment="Center">
        <ComboBoxItem Content="Tốt nhất có thể (MP4)"/>
        <ComboBoxItem Content="Tối đa 1080p"/>
        <ComboBoxItem Content="Tối đa 720p"/>
        <ComboBoxItem Content="Tối đa 480p"/>
        <ComboBoxItem Content="Chỉ lấy âm thanh (MP3)"/>
      </ComboBox>
      <CheckBox x:Name="chkPlaylist" Content="Tải cả playlist"/>
      <CheckBox x:Name="chkCookie" Content="Dùng cookie:"/>
      <ComboBox x:Name="cboBrowser" Width="80" FontSize="13" SelectedIndex="0" Margin="6,0,0,0" VerticalAlignment="Center">
        <ComboBoxItem Content="edge"/>
        <ComboBoxItem Content="chrome"/>
        <ComboBoxItem Content="firefox"/>
      </ComboBox>
      <Button x:Name="btnCapture" Content="🌐 Bắt video từ trang web" Background="#F9E2AF" Margin="16,0,0,0"/>
    </StackPanel>

    <Grid Grid.Row="2" Margin="0,0,0,12">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBlock Grid.Column="0" Text="Lưu vào:" Margin="0,0,10,0" FontWeight="Bold"/>
      <TextBox x:Name="txtFolder" Grid.Column="1"/>
      <Button x:Name="btnFolder" Grid.Column="2" Content="Chọn..."/>
      <Button x:Name="btnOpen" Grid.Column="3" Content="Mở thư mục" Background="#45475A" Foreground="#CDD6F4"/>
    </Grid>

    <Grid Grid.Row="3" Margin="0,0,0,12">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <Button x:Name="btnDownload" Grid.Column="0" Content="⬇  TẢI XUỐNG" FontSize="17" Padding="0,10" Margin="0" Background="#A6E3A1"/>
      <Button x:Name="btnUpdate" Grid.Column="1" Content="Cập nhật engine" Background="#45475A" Foreground="#CDD6F4"/>
    </Grid>

    <StackPanel Grid.Row="4" Margin="0,0,0,10">
      <ProgressBar x:Name="pb" Height="18" Minimum="0" Maximum="100" Background="#313244" Foreground="#A6E3A1" BorderThickness="0"/>
      <TextBlock x:Name="lblStatus" Text="Sẵn sàng — dán link rồi bấm TẢI XUỐNG (hoặc Enter)." Margin="0,6,0,0" FontSize="13" Foreground="#A6ADC8"/>
    </StackPanel>

    <TextBox x:Name="txtLog" Grid.Row="5" IsReadOnly="True" TextWrapping="Wrap"
             VerticalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12"
             Background="#181825" Foreground="#A6ADC8" BorderBrush="#313244"/>
  </Grid>
</Window>
'@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)

$txtUrl      = $window.FindName('txtUrl')
$btnPaste    = $window.FindName('btnPaste')
$cboQuality  = $window.FindName('cboQuality')
$chkPlaylist = $window.FindName('chkPlaylist')
$chkCookie   = $window.FindName('chkCookie')
$cboBrowser  = $window.FindName('cboBrowser')
$txtFolder   = $window.FindName('txtFolder')
$btnFolder   = $window.FindName('btnFolder')
$btnOpen     = $window.FindName('btnOpen')
$btnDownload = $window.FindName('btnDownload')
$btnUpdate   = $window.FindName('btnUpdate')
$pb          = $window.FindName('pb')
$lblStatus   = $window.FindName('lblStatus')
$txtLog      = $window.FindName('txtLog')
$btnCapture  = $window.FindName('btnCapture')

# link bắt được từ trình duyệt nhúng (kèm header của chính phiên duyệt đó)
$script:capUrl    = ''
$script:capRef    = ''
$script:capCookie = ''
$script:capUA     = ''
$script:capTitle  = ''   # tiêu đề trang -> dùng làm tên file cho link tải trực tiếp

# ---------- cấu hình mặc định ----------
if (Test-Path 'D:\vid') { $txtFolder.Text = 'D:\vid' }
else { $txtFolder.Text = [Environment]::GetFolderPath('MyVideos') }

if (Test-Path $cfg) {
    try {
        $s = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($s.folder -and (Test-Path $s.folder)) { $txtFolder.Text = $s.folder }
        if ($null -ne $s.quality) { $cboQuality.SelectedIndex = [int]$s.quality }
    } catch { }
}

function Save-Settings {
    try {
        @{ folder = $txtFolder.Text; quality = $cboQuality.SelectedIndex } |
            ConvertTo-Json | Set-Content -Path $cfg -Encoding UTF8
    } catch { }
}

# tự điền link nếu clipboard đang có sẵn
try {
    $clip = [Windows.Clipboard]::GetText()
    if ($clip -and $clip.Trim() -match '^https?://\S+$') { $txtUrl.Text = $clip.Trim() }
} catch { }

# ---------- chạy yt-dlp ----------
$script:proc      = $null
$script:outLog    = $null
$script:errLog    = $null
$script:cancelled = $false

function Read-SharedFile([string]$path) {
    if (-not (Test-Path $path)) { return '' }
    try {
        $fs = [System.IO.File]::Open($path, 'Open', 'Read', [System.IO.FileShare]::ReadWrite)
        try {
            $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
            return $sr.ReadToEnd()
        } finally { $fs.Close() }
    } catch { return '' }
}

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(500)

function Start-Ytdlp {
    param([string[]]$ArgList, [string]$Status)
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $script:outLog = Join-Path $logDir "out_$stamp.log"
    $script:errLog = Join-Path $logDir "err_$stamp.log"
    $script:cancelled = $false
    # bắt buộc: không có cờ này yt-dlp ghi log theo bảng mã ANSI -> tên tiếng Việt trong log hỏng hết
    $ArgList = @('--encoding', 'utf-8') + $ArgList
    $quoted = ($ArgList | ForEach-Object {
        if ($_ -match '[\s"&]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
    $script:proc = Start-Process -FilePath $ytdlp -ArgumentList $quoted -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $script:outLog -RedirectStandardError $script:errLog
    $lblStatus.Text = $Status
    $pb.IsIndeterminate = $true
    $txtLog.Text = ''
    $btnDownload.Content = '✖  HỦY'
    $btnDownload.Background = '#F38BA8'
    $timer.Start()
}

$timer.Add_Tick({
    try {
        $text = Read-SharedFile $script:outLog
        if ($text) {
            $show = $text
            if ($show.Length -gt 8000) { $show = $show.Substring($show.Length - 8000) }
            $txtLog.Text = $show
            $txtLog.ScrollToEnd()

            $m = [regex]::Matches($text, '\[download\]\s+([0-9.]+)%')
            if ($m.Count -gt 0) {
                $pb.IsIndeterminate = $false
                $pb.Value = [double]$m[$m.Count - 1].Groups[1].Value
            }
            $lines = @($text -split "`r?`n" | Where-Object { $_.Trim() })
            if ($lines.Count -gt 0) {
                $last = $lines[$lines.Count - 1]
                if ($last -match '\[(Merger|ExtractAudio|VideoConvertor|FixupM3u8)\]') {
                    $pb.IsIndeterminate = $true
                    $lblStatus.Text = 'Đang ghép / chuyển đổi file...'
                } else {
                    if ($last.Length -gt 110) { $last = $last.Substring(0, 110) + '...' }
                    $lblStatus.Text = $last
                }
            }
        }
        if ($script:proc -and $script:proc.HasExited) {
            $timer.Stop()
            $pb.IsIndeterminate = $false
            $err = Read-SharedFile $script:errLog
            # Start-Process -PassThru KHÔNG trả về ExitCode dùng được (luôn null, cả khi thành công),
            # nên phải xét kết quả qua dòng "ERROR:" mà yt-dlp ghi ra stderr.
            $failed = [bool]($err -match '(?m)^ERROR:')
            if ($script:cancelled) {
                $lblStatus.Text = 'Đã hủy tải.'
            } elseif (-not $failed) {
                $pb.Value = 100
                # lấy tên file thật từ log để báo cho chắc chắn
                $saved = ''
                try {
                    $md = [regex]::Matches($text, '(?m)^\[[^\]]+\] Destination: (.+?)\s*$')
                    if ($md.Count -gt 0) { $saved = $md[$md.Count - 1].Groups[1].Value }
                    $mg = [regex]::Matches($text, '(?m)^\[Merger\] Merging formats into "(.+?)"\s*$')
                    if ($mg.Count -gt 0) { $saved = $mg[$mg.Count - 1].Groups[1].Value }
                    if ($saved) { $saved = [System.IO.Path]::GetFileName($saved) }
                } catch {}
                if ($saved) { $lblStatus.Text = '✅ Xong! Đã lưu: ' + $saved }
                else        { $lblStatus.Text = '✅ Xong! File đã lưu vào thư mục.' }
            } else {
                $lblStatus.Text = '❌ Có lỗi — xem chi tiết bên dưới.'
                if ($err) { $txtLog.Text += "`r`n--- LỖI ---`r`n" + $err; $txtLog.ScrollToEnd() }
            }
            $btnDownload.Content = '⬇  TẢI XUỐNG'
            $btnDownload.Background = '#A6E3A1'
            $script:proc = $null
        }
    } catch { }
})

function Invoke-DownloadClick {
    # đang tải → nút này là nút Hủy
    if ($script:proc -and -not $script:proc.HasExited) {
        $script:cancelled = $true
        Start-Process -FilePath 'taskkill' -ArgumentList "/PID $($script:proc.Id) /T /F" -WindowStyle Hidden
        $lblStatus.Text = 'Đã hủy tải.'
        return
    }
    $url = $txtUrl.Text.Trim()
    if (-not $url -or $url -notmatch '^https?://') {
        [System.Windows.MessageBox]::Show('Hãy dán link video vào ô (bắt đầu bằng http/https).', 'Tải Video', 'OK', 'Warning') | Out-Null
        return
    }
    # Link nhúng (Blogger...): yt-dlp không đọc được -> tự mở player lấy link video thật
    if ($url -match $script:capEmbedPat) {
        $lblStatus.Text = 'Link nhúng — đang mở player để lấy link video thật...'
        $rv = Resolve-EmbedUrl -EmbedUrl $url -Referer $script:capRef
        if (-not $rv) {
            $lblStatus.Text = '❌ Không lấy được link video thật từ trang nhúng.'
            [System.Windows.MessageBox]::Show(
                'Không lấy được link video thật từ trang nhúng này.' + [Environment]::NewLine + [Environment]::NewLine +
                'Cách khác: bấm 🌐 Bắt video từ trang web, mở trang gốc, bấm play rồi chọn link [MP4] hoặc [HLS].',
                'Tải Video', 'OK', 'Warning') | Out-Null
            return
        }
        $script:capUrl    = [string]$rv.url
        $script:capRef    = [string]$rv.ref
        $script:capCookie = [string]$rv.cookie
        $script:capUA     = [string]$rv.ua
        if (-not $script:capTitle) { $script:capTitle = 'video_' + (Get-Date -Format 'yyyyMMdd_HHmmss') }
        $url = $script:capUrl
        $txtUrl.Text = $url
        $lblStatus.Text = '✅ Đã lấy được link thật, bắt đầu tải...'
    }

    $folder = $txtFolder.Text.Trim()
    if (-not $folder) { $folder = [Environment]::GetFolderPath('MyVideos'); $txtFolder.Text = $folder }
    if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    Save-Settings

    $a = New-Object System.Collections.Generic.List[string]
    $a.Add('--newline'); $a.Add('--no-mtime'); $a.Add('--windows-filenames')
    $a.Add('--ffmpeg-location'); $a.Add($bin)
    $a.Add('-o')
    if ($script:capUrl -and ($url -eq $script:capUrl) -and $script:capTitle) {
        # link bắt trực tiếp không có tên -> đặt theo tiêu đề trang cho dễ tìm
        $a.Add((Join-Path $folder ($script:capTitle + '.%(ext)s')))
    } else {
        $a.Add((Join-Path $folder '%(title)s.%(ext)s'))
    }
    switch ($cboQuality.SelectedIndex) {
        0 { $a.Add('-f'); $a.Add('bestvideo+bestaudio/best'); $a.Add('--merge-output-format'); $a.Add('mp4') }
        1 { $a.Add('-f'); $a.Add('bestvideo[height<=1080]+bestaudio/best[height<=1080]/best'); $a.Add('--merge-output-format'); $a.Add('mp4') }
        2 { $a.Add('-f'); $a.Add('bestvideo[height<=720]+bestaudio/best[height<=720]/best'); $a.Add('--merge-output-format'); $a.Add('mp4') }
        3 { $a.Add('-f'); $a.Add('bestvideo[height<=480]+bestaudio/best[height<=480]/best'); $a.Add('--merge-output-format'); $a.Add('mp4') }
        4 { $a.Add('-x'); $a.Add('--audio-format'); $a.Add('mp3'); $a.Add('--audio-quality'); $a.Add('0') }
    }
    if (-not $chkPlaylist.IsChecked) { $a.Add('--no-playlist') }
    if ($chkCookie.IsChecked) {
        $a.Add('--cookies-from-browser'); $a.Add([string]$cboBrowser.SelectedItem.Content)
    }
    # nếu link này là link vừa bắt từ trình duyệt nhúng → gắn đúng header phiên đó
    if ($script:capUrl -and ($url -eq $script:capUrl)) {
        if ($script:capRef)    { $a.Add('--referer');    $a.Add($script:capRef) }
        if ($script:capUA)     { $a.Add('--user-agent'); $a.Add($script:capUA) }
        if ($script:capCookie) { $a.Add('--add-header');  $a.Add("Cookie: $($script:capCookie)") }
    }
    $a.Add($url)
    Start-Ytdlp -ArgList $a.ToArray() -Status 'Đang lấy thông tin video...'
}

# ---------- trình duyệt nhúng bắt link (giống Video DownloadHelper) ----------
$script:wv2Loaded = $false
function Initialize-WebView2 {
    if ($script:wv2Loaded) { return $true }
    try {
        Add-Type -Path (Join-Path $bin 'Microsoft.Web.WebView2.Core.dll')
        Add-Type -Path (Join-Path $bin 'Microsoft.Web.WebView2.Wpf.dll')
        $script:wv2Loaded = $true
        return $true
    } catch {
        [System.Windows.MessageBox]::Show("Không nạp được WebView2.`nThiếu file trong bin\ (Microsoft.Web.WebView2.*.dll, WebView2Loader.dll).`n`n$($_.Exception.Message)", 'Tải Video', 'OK', 'Error') | Out-Null
        return $false
    }
}

# link nhúng: trang player trung gian, yt-dlp không đọc được -> phải mở bằng trình duyệt mới ra link thật
$script:capEmbedPat = 'blogger\.com/video\.g'

# host quảng cáo — bỏ qua để không lẫn video ads vào danh sách.
# LƯU Ý: phải khai báo ở phạm vi chung; nếu để $null thì "$uri -match $null" luôn đúng -> loại sạch mọi link.
$script:capAdHosts = 'vietadx|doubleclick|googlesyndication|adsystem|adnxs|popads|exoclick|juicyads|trafficjunky'

# mảnh stream lẻ (HLS/DASH cắt nhỏ hàng trăm file) — bỏ qua, chỉ giữ link .m3u8 gốc
$script:capIsSegment = {
    param($u)
    if ($u -match '\.(ts|m4s)(\?|#|$)') { return $true }
    if ($u -match '(seg|segment|chunk|frag)[-_/]?\d+') { return $true }
    return $false
}

# Mở player nhúng trong nền, tự phát để lấy link video thật (googlevideo/m3u8/mp4).
# Trả về hashtable @{url; ref; cookie; ua} hoặc $null nếu không lấy được.
function Resolve-EmbedUrl {
    param([string]$EmbedUrl, [string]$Referer)

    if (-not (Initialize-WebView2)) { return $null }

    $script:rsvResult = $null
    $script:rsvSeen   = New-Object System.Collections.Generic.HashSet[string]
    # bật gỡ lỗi: đặt biến môi trường TAIVIDEO_DEBUG=1 -> ghi logs\resolve_debug.log
    $script:rsvLog = New-Object System.Text.StringBuilder
    $script:rsvL   = {
        param($m)
        if ($env:TAIVIDEO_DEBUG -eq '1') {
            [void]$script:rsvLog.AppendLine((Get-Date -Format 'HH:mm:ss.fff') + '  ' + $m)
        }
    }

    [xml]$rx = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Đang lấy link video thật..." Height="620" Width="900"
        WindowStartupLocation="CenterScreen" Background="#1E1E2E">
  <Grid Margin="10">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>
    <TextBlock x:Name="rStatus" Grid.Row="0" Foreground="#F9E2AF" FontSize="13" Margin="0,0,0,8"
               TextWrapping="Wrap"
               Text="Đang mở player để lấy link video thật. Chờ vài giây, cửa sổ này tự đóng..."/>
    <Border x:Name="rHost" Grid.Row="1" BorderBrush="#45475A" BorderThickness="1" Background="#000000"/>
  </Grid>
</Window>
'@
    $rr = New-Object System.Xml.XmlNodeReader $rx
    $script:rsvWin = [Windows.Markup.XamlReader]::Load($rr)
    $rStatus = $script:rsvWin.FindName('rStatus')
    $rHost   = $script:rsvWin.FindName('rHost')

    $script:rsvWv = New-Object Microsoft.Web.WebView2.Wpf.WebView2
    $rp = New-Object Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
    $rp.UserDataFolder = (Join-Path $root 'wvdata')
    $script:rsvWv.CreationProperties = $rp
    $rHost.Child = $script:rsvWv

    $script:rsvTake = {
        param($uri, $headers, $kind)
        try {
            if ($script:rsvResult) { return }
            if (-not $uri) { return }
            if ($uri -match $script:capEmbedPat) { return }   # bỏ qua chính trang nhúng
            if ($script:rsvSeen.Contains($uri)) { return }
            [void]$script:rsvSeen.Add($uri)
            $ref = ''; $ck = ''; $ua = ''
            if ($headers) {
                try { if ($headers.Contains('Referer'))    { $ref = $headers.GetHeader('Referer') } }    catch {}
                try { if ($headers.Contains('Cookie'))     { $ck  = $headers.GetHeader('Cookie') } }     catch {}
                try { if ($headers.Contains('User-Agent')) { $ua  = $headers.GetHeader('User-Agent') } } catch {}
            }
            $script:rsvResult = @{ url = $uri; ref = $ref; cookie = $ck; ua = $ua; kind = $kind }
            & $script:rsvL ("*** MEDIA $kind : " + $uri.Substring(0, [Math]::Min(120, $uri.Length)))
        } catch {}
    }
    $script:rsvOnReq = {
        param($s, $e)
        try {
            $uri = $e.Request.Uri
            & $script:rsvL ('req: ' + $uri.Substring(0, [Math]::Min(130, $uri.Length)))
            if ($uri -match '/cdn-cgi/' -or $uri -match $script:capAdHosts) { return }
            if (& $script:capIsSegment $uri) { return }
            $kind = ''
            if     ($uri -match '\.m3u8(\?|#|$)')                 { $kind = 'HLS' }
            elseif ($uri -match '\.mpd(\?|#|$)')                  { $kind = 'DASH' }
            elseif ($uri -match '\.(mp4|m4v|webm|mov)(\?|#|$)')   { $kind = 'MP4' }
            elseif ($uri -match 'googlevideo\.com/videoplayback') { $kind = 'MP4' }
            if (-not $kind) { return }
            & $script:rsvTake $uri $e.Request.Headers $kind
        } catch {}
    }
    $script:rsvOnResp = {
        param($s, $e)
        try {
            $uri = $e.Request.Uri
            if ($uri -match '/cdn-cgi/' -or $uri -match $script:capAdHosts) { return }
            if (& $script:capIsSegment $uri) { return }
            $ct = ''
            try { $ct = $e.Response.Headers.GetHeader('Content-Type') } catch { return }
            if (-not $ct) { return }
            $kind = ''
            if     ($ct -match 'mpegurl')   { $kind = 'HLS' }
            elseif ($ct -match 'dash\+xml') { $kind = 'DASH' }
            elseif ($ct -match '^video/')   { $kind = 'MP4' }
            if (-not $kind) { return }
            & $script:rsvTake $uri $e.Request.Headers $kind
        } catch {}
    }

    $script:rsvWv.add_CoreWebView2InitializationCompleted({
        param($s, $e)
        if (-not $e.IsSuccess) {
            & $script:rsvL ('init FAIL: ' + $e.InitializationException.Message)
            $script:rsvWin.Close(); return
        }
        & $script:rsvL 'init OK'
        $cw = $script:rsvWv.CoreWebView2
        $ctxAll = [Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext]::All
        try {
            $srcAll = [Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestSourceKinds]::All
            $cw.AddWebResourceRequestedFilter('*', $ctxAll, $srcAll)
            & $script:rsvL 'filter 3-arg OOPIF'
        } catch {
            $cw.AddWebResourceRequestedFilter('*', $ctxAll)
            & $script:rsvL 'filter 2-arg fallback'
        }
        $cw.add_WebResourceRequested($script:rsvOnReq)
        try { $cw.add_WebResourceResponseReceived($script:rsvOnResp); & $script:rsvL 'resp hook OK' }
        catch { & $script:rsvL 'resp hook FAIL' }
        $cw.add_NavigationCompleted({ param($a, $b) & $script:rsvL ('nav done success=' + $b.IsSuccess) })
        try { $cw.Navigate($EmbedUrl); & $script:rsvL 'navigating' }
        catch { & $script:rsvL ('navigate err: ' + $_.Exception.Message); $script:rsvWin.Close() }
    })
    $script:rsvWin.Add_Loaded({
        try { [void]$script:rsvWv.EnsureCoreWebView2Async($null); & $script:rsvL 'ensure called' }
        catch { & $script:rsvL ('ensure err: ' + $_.Exception.Message); $script:rsvWin.Close() }
    })

    # tự gọi play() vài lần (player nhúng không có quảng cáo nên an toàn), tối đa 30 giây
    $rt0 = Get-Date
    $rtimer = New-Object System.Windows.Threading.DispatcherTimer
    $rtimer.Interval = [TimeSpan]::FromSeconds(1)
    # gọi play() + click vào giữa khung (player Blogger chỉ dựng thẻ <video> sau khi có tương tác)
    $script:rsvPlay = {
        if (-not $script:rsvWv.CoreWebView2) { return }
        $js = "(function(){var v=document.querySelector('video');if(v){v.muted=true;v.play();return 'ok';}return 'no';})()"
        try { [void]$script:rsvWv.CoreWebView2.CallDevToolsProtocolMethodAsync('Runtime.evaluate', (@{ expression = $js } | ConvertTo-Json -Compress)) } catch {}
    }
    $script:rsvClick = {
        if (-not $script:rsvWv.CoreWebView2) { return }
        $x = [int]($script:rsvWv.ActualWidth / 2)
        $y = [int]($script:rsvWv.ActualHeight / 2)
        if ($x -le 0 -or $y -le 0) { return }
        foreach ($ty in 'mouseMoved', 'mousePressed', 'mouseReleased') {
            $p = if ($ty -eq 'mouseMoved') { @{ type = $ty; x = $x; y = $y } }
                 else { @{ type = $ty; x = $x; y = $y; button = 'left'; clickCount = 1 } }
            try { [void]$script:rsvWv.CoreWebView2.CallDevToolsProtocolMethodAsync('Input.dispatchMouseEvent', ($p | ConvertTo-Json -Compress)) } catch {}
        }
    }
    $rtimer.Add_Tick({
        $el = ((Get-Date) - $rt0).TotalSeconds
        if ($script:rsvResult) { $rtimer.Stop(); $script:rsvWin.Close(); return }
        if     ($el -ge 3  -and $el -lt 4)  { & $script:rsvPlay }
        elseif ($el -ge 6  -and $el -lt 7)  { & $script:rsvClick }
        elseif ($el -ge 10 -and $el -lt 11) { & $script:rsvPlay; & $script:rsvClick }
        elseif ($el -ge 15 -and $el -lt 16) { & $script:rsvClick }
        elseif ($el -ge 20 -and $el -lt 21) { & $script:rsvPlay; & $script:rsvClick }
        if ($el -ge 12 -and $el -lt 13) { $rStatus.Text = 'Vẫn đang thử... nếu thấy nút ▶ trong khung dưới, bạn bấm giúp một cái.' }
        if ($el -ge 32) { $rtimer.Stop(); $script:rsvWin.Close() }
    })
    $rtimer.Start()

    $script:rsvWin.Add_Closed({
        try { $rtimer.Stop() } catch {}
        try { $script:rsvWv.Dispose() } catch {}
        if ($env:TAIVIDEO_DEBUG -eq '1') {
            try { [System.IO.File]::WriteAllText((Join-Path $logDir 'resolve_debug.log'), $script:rsvLog.ToString(), [System.Text.Encoding]::UTF8) } catch {}
        }
    })
    try { if ($window.IsVisible) { $script:rsvWin.Owner = $window } } catch {}
    $script:rsvWin.ShowDialog() | Out-Null

    return $script:rsvResult
}

function Open-CaptureWindow {
    param([string]$StartUrl)

    if (-not (Initialize-WebView2)) { return }

    # bộ nhớ link bắt được (script-scope để handler đọc/ghi ổn định)
    $script:capCol  = New-Object System.Collections.ObjectModel.ObservableCollection[string]
    $script:capMap  = @{}
    $script:capSeen = New-Object System.Collections.Generic.HashSet[string]


    [xml]$cx = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Bắt video từ trang web — play video rồi chọn link" Height="760" Width="1040"
        WindowStartupLocation="CenterScreen" Background="#1E1E2E">
  <Grid Margin="8">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    <Grid Grid.Row="0" Margin="0,0,0,6">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <Button x:Name="cBack" Grid.Column="0" Content="◀" Width="38" Background="#45475A" Foreground="#CDD6F4" FontWeight="Bold"/>
      <TextBox x:Name="cAddr" Grid.Column="1" Margin="6,0,6,0" FontSize="13" Padding="8,6"
               Background="#313244" Foreground="#CDD6F4" BorderBrush="#45475A"/>
      <Button x:Name="cGo" Grid.Column="2" Content="Đi" Width="52" Background="#89B4FA" Foreground="#11111B" FontWeight="Bold"/>
    </Grid>
    <Border x:Name="cHost" Grid.Row="1" BorderBrush="#45475A" BorderThickness="1" Background="#000000"/>
    <Grid Grid.Row="2" Margin="0,8,0,4">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBlock x:Name="cInfo" Grid.Column="0" Foreground="#F9E2AF" FontSize="13" VerticalAlignment="Center"
                 Text="Bấm ▶ play video trong trang, chờ 2-3 giây → link video sẽ hiện ở danh sách dưới → chọn rồi bấm nút xanh."/>
      <TextBlock x:Name="cCount" Grid.Column="1" Foreground="#A6ADC8" FontSize="13" VerticalAlignment="Center"/>
    </Grid>
    <Grid Grid.Row="3">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <ListBox x:Name="cList" Grid.Column="0" Height="96" FontFamily="Consolas" FontSize="12"
               Background="#181825" Foreground="#A6E3A1" BorderBrush="#313244"/>
      <Button x:Name="cCopy" Grid.Column="1" Content="Copy" Width="72" VerticalAlignment="Top" Margin="6,0,0,0"
              Background="#45475A" Foreground="#CDD6F4"/>
      <Button x:Name="cUse" Grid.Column="2" Content="⬇ Dùng link này để TẢI" VerticalAlignment="Top" Margin="6,0,0,0"
              Padding="12,8" Background="#A6E3A1" Foreground="#11111B" FontWeight="Bold"/>
    </Grid>
  </Grid>
</Window>
'@
    $cr = New-Object System.Xml.XmlNodeReader $cx
    $script:capWin    = [Windows.Markup.XamlReader]::Load($cr)
    $script:capAddr   = $script:capWin.FindName('cAddr')
    $script:capList   = $script:capWin.FindName('cList')
    $script:capCount  = $script:capWin.FindName('cCount')
    $cHost            = $script:capWin.FindName('cHost')
    $cBack            = $script:capWin.FindName('cBack')
    $cGo              = $script:capWin.FindName('cGo')
    $cUse             = $script:capWin.FindName('cUse')
    $cCopy            = $script:capWin.FindName('cCopy')

    $script:capList.ItemsSource = $script:capCol
    if ($StartUrl -and $StartUrl -match '^https?://') { $script:capAddr.Text = $StartUrl }
    else { $script:capAddr.Text = 'https://' }

    $script:capWv = New-Object Microsoft.Web.WebView2.Wpf.WebView2
    $cprops = New-Object Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
    $cprops.UserDataFolder = (Join-Path $root 'wvdata')
    $script:capWv.CreationProperties = $cprops
    $cHost.Child = $script:capWv

    # thêm 1 link vào danh sách (dùng chung cho handler request + response)
    $script:capAdd = {
        param($uri, $headers, $kind)
        try {
            if (-not $uri) { return }
            if ($script:capSeen.Contains($uri)) { return }
            [void]$script:capSeen.Add($uri)
            $ref = ''; $ck = ''; $ua = ''
            if ($headers) {
                try { if ($headers.Contains('Referer'))    { $ref = $headers.GetHeader('Referer') } }    catch {}
                try { if ($headers.Contains('Cookie'))     { $ck  = $headers.GetHeader('Cookie') } }     catch {}
                try { if ($headers.Contains('User-Agent')) { $ua  = $headers.GetHeader('User-Agent') } } catch {}
            }
            $hst = ''
            try { $hst = ([Uri]$uri).Host } catch {}
            $short = $uri
            if ($short.Length -gt 110) { $short = $short.Substring(0, 110) + '...' }
            $disp = "[$kind] $hst   $short"
            $script:capMap[$disp] = @{ url = $uri; ref = $ref; cookie = $ck; ua = $ua }
            $script:capCol.Add($disp)
            $script:capCount.Text = "$($script:capCol.Count) link bắt được"
            if ($script:capList.SelectedIndex -lt 0) { $script:capList.SelectedIndex = 0 }
        } catch {}
    }

    # handler 1: nhận diện theo dạng URL (bắt được cả trong iframe khác domain)
    $script:capOnReq = {
        param($s, $e)
        try {
            $uri = $e.Request.Uri
            if ($uri -match '/cdn-cgi/') { return }
            if ($uri -match $script:capAdHosts) { return }
            if (& $script:capIsSegment $uri) { return }
            $kind = ''
            if     ($uri -match '\.m3u8(\?|#|$)')                { $kind = 'HLS' }
            elseif ($uri -match '\.mpd(\?|#|$)')                 { $kind = 'DASH' }
            elseif ($uri -match '\.(mp4|m4v|webm|mov)(\?|#|$)')  { $kind = 'MP4' }
            elseif ($uri -match 'googlevideo\.com/videoplayback'){ $kind = 'MP4' }
            elseif ($uri -match 'blogger\.com/video\.g')         { $kind = 'NHUNG' }
            if (-not $kind) { return }
            & $script:capAdd $uri $e.Request.Headers $kind
        } catch {}
    }

    # handler 2: nhận diện theo Content-Type — bắt được cả link không có đuôi file
    $script:capOnResp = {
        param($s, $e)
        try {
            $uri = $e.Request.Uri
            if ($uri -match '/cdn-cgi/') { return }
            if ($uri -match $script:capAdHosts) { return }
            if (& $script:capIsSegment $uri) { return }
            $ct = ''
            try { $ct = $e.Response.Headers.GetHeader('Content-Type') } catch { return }
            if (-not $ct) { return }
            $kind = ''
            if     ($ct -match 'mpegurl')   { $kind = 'HLS' }
            elseif ($ct -match 'dash\+xml') { $kind = 'DASH' }
            elseif ($ct -match '^video/')   { $kind = 'MP4' }
            if (-not $kind) { return }
            & $script:capAdd $uri $e.Request.Headers $kind
        } catch {}
    }

    $script:capWv.add_CoreWebView2InitializationCompleted({
        param($s, $e)
        if (-not $e.IsSuccess) {
            $script:capCount.Text = 'Lỗi khởi tạo WebView2'
            return
        }
        $cw = $script:capWv.CoreWebView2
        $ctxAll = [Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext]::All
        # 3 tham số = bắt được cả request bên trong iframe khác domain (OOPIF) — rất quan trọng
        try {
            $srcAll = [Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestSourceKinds]::All
            $cw.AddWebResourceRequestedFilter('*', $ctxAll, $srcAll)
        } catch {
            $cw.AddWebResourceRequestedFilter('*', $ctxAll)
        }
        $cw.add_WebResourceRequested($script:capOnReq)
        try { $cw.add_WebResourceResponseReceived($script:capOnResp) } catch {}
        $cw.add_SourceChanged({ try { $script:capAddr.Text = $script:capWv.Source.ToString() } catch {} })
        $u = $script:capAddr.Text.Trim()
        if ($u -match '^https?://\S+$' -and $u -ne 'https://') { try { $cw.Navigate($u) } catch {} }
    })
    $script:capWin.Add_Loaded({
        try { [void]$script:capWv.EnsureCoreWebView2Async($null) }
        catch { $script:capCount.Text = 'Lỗi WebView2: ' + $_.Exception.Message }
    })

    $goNav = {
        $u = $script:capAddr.Text.Trim()
        if ($u -notmatch '^https?://') { $u = 'https://' + $u }
        if ($script:capWv.CoreWebView2) { try { $script:capWv.CoreWebView2.Navigate($u) } catch {} }
    }
    $cGo.Add_Click($goNav)
    $script:capAddr.Add_KeyDown({ param($s, $e) if ($e.Key -eq 'Return') { & $goNav } })
    $cBack.Add_Click({
        if ($script:capWv.CoreWebView2 -and $script:capWv.CoreWebView2.CanGoBack) { $script:capWv.CoreWebView2.GoBack() }
    })
    $cCopy.Add_Click({
        $sel = $script:capList.SelectedItem
        if (-not $sel) { return }
        $info = $script:capMap[[string]$sel]
        if ($info) { try { [Windows.Clipboard]::SetText([string]$info.url) } catch {} }
    })
    $cUse.Add_Click({
        $sel = $script:capList.SelectedItem
        if (-not $sel -and $script:capCol.Count -gt 0) {
            # chưa chọn gì: ưu tiên HLS > MP4 > còn lại (link nhúng xếp cuối)
            $hls = @($script:capCol | Where-Object { $_ -like '`[HLS`]*' })
            $mp4 = @($script:capCol | Where-Object { $_ -like '`[MP4`]*' })
            $sel = if ($hls.Count -gt 0) { $hls[0] }
                   elseif ($mp4.Count -gt 0) { $mp4[$mp4.Count - 1] }
                   else { $script:capCol[$script:capCol.Count - 1] }
        }
        if (-not $sel) {
            [System.Windows.MessageBox]::Show('Chưa bắt được link nào.' + [Environment]::NewLine + 'Hãy bấm ▶ play video trong trang rồi chờ vài giây.', 'Bắt video', 'OK', 'Warning') | Out-Null
            return
        }
        $info = $script:capMap[[string]$sel]
        if (-not $info) { return }
        $script:capUrl    = [string]$info.url
        $script:capRef    = [string]$info.ref
        $script:capCookie = [string]$info.cookie
        $script:capUA     = [string]$info.ua
        # tiêu đề trang -> tên file (link stream trực tiếp không mang tên video)
        $t = ''
        try { $t = $script:capWv.CoreWebView2.DocumentTitle } catch {}
        if ($t) {
            $t = $t -replace '[\\/:*?"<>|%]', ' '
            $t = ($t -replace '\s+', ' ').Trim()
            if ($t.Length -gt 120) { $t = $t.Substring(0, 120).Trim() }
        }
        $script:capTitle = $t
        $txtUrl.Text = $script:capUrl
        $lblStatus.Text = '✅ Đã lấy link từ trình duyệt. Bấm TẢI XUỐNG.'
        $script:capWin.Close()
    })
    $script:capWin.Add_Closed({ try { $script:capWv.Dispose() } catch {} })
    try { if ($window.IsVisible) { $script:capWin.Owner = $window } } catch {}
    $script:capWin.ShowDialog() | Out-Null
}

# ---------- gắn sự kiện ----------
$btnCapture.Add_Click({
    $seed = $txtUrl.Text.Trim()
    Open-CaptureWindow -StartUrl $seed
})
$btnPaste.Add_Click({
    try { $t = [Windows.Clipboard]::GetText(); if ($t) { $txtUrl.Text = $t.Trim() } } catch { }
})
$txtUrl.Add_KeyDown({ param($s, $e) if ($e.Key -eq 'Return') { Invoke-DownloadClick } })
$btnDownload.Add_Click({ Invoke-DownloadClick })
$btnFolder.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Chọn thư mục lưu video'
    if (Test-Path $txtFolder.Text) { $dlg.SelectedPath = $txtFolder.Text }
    if ($dlg.ShowDialog() -eq 'OK') { $txtFolder.Text = $dlg.SelectedPath; Save-Settings }
})
$btnOpen.Add_Click({
    $f = $txtFolder.Text.Trim()
    if (Test-Path $f) { Start-Process explorer.exe $f }
})
$btnUpdate.Add_Click({
    if ($script:proc -and -not $script:proc.HasExited) { return }
    Start-Ytdlp -ArgList @('-U') -Status 'Đang cập nhật yt-dlp lên bản mới nhất...'
})
$window.Add_Closing({
    Save-Settings
    if ($script:proc -and -not $script:proc.HasExited) {
        Start-Process -FilePath 'taskkill' -ArgumentList "/PID $($script:proc.Id) /T /F" -WindowStyle Hidden
    }
})

if ($env:TAIVIDEO_TEST -ne '1') { $window.ShowDialog() | Out-Null }

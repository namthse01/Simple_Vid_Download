# =============================================================
#  Tải Video — cài đặt engine vào thư mục bin\
#  Chạy 1 lần sau khi clone:  chuột phải > Run with PowerShell
#  (hoặc: powershell -ExecutionPolicy Bypass -File setup.ps1)
# =============================================================

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin  = Join-Path $root 'bin'
$tmp  = Join-Path $env:TEMP ('taivideo_setup_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $bin, $tmp -Force | Out-Null

function Say([string]$m) { Write-Host $m -ForegroundColor Cyan }
function Ok ([string]$m) { Write-Host ('  OK  ' + $m) -ForegroundColor Green }

try {
    # ---- 1. yt-dlp: engine tải video ----
    Say 'Dang tai yt-dlp...'
    Invoke-WebRequest -Uri 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' `
        -OutFile (Join-Path $bin 'yt-dlp.exe') -UseBasicParsing
    Ok ('yt-dlp ' + (& (Join-Path $bin 'yt-dlp.exe') --version))

    # ---- 2. ffmpeg + ffprobe: ghep video/audio, convert MP3 ----
    Say 'Dang tai ffmpeg (~170MB, hoi lau)...'
    $ffZip = Join-Path $tmp 'ffmpeg.zip'
    Invoke-WebRequest -Uri 'https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip' `
        -OutFile $ffZip -UseBasicParsing
    Expand-Archive -Path $ffZip -DestinationPath (Join-Path $tmp 'ff') -Force
    foreach ($exe in 'ffmpeg.exe', 'ffprobe.exe') {
        $src = Get-ChildItem (Join-Path $tmp 'ff') -Recurse -Filter $exe | Select-Object -First 1
        Copy-Item $src.FullName (Join-Path $bin $exe) -Force
    }
    Ok ((& (Join-Path $bin 'ffmpeg.exe') -version)[0])

    # ---- 3. deno: JS runtime, yt-dlp can de lay YouTube chat luong cao ----
    Say 'Dang tai deno...'
    $dZip = Join-Path $tmp 'deno.zip'
    Invoke-WebRequest -Uri 'https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip' `
        -OutFile $dZip -UseBasicParsing
    Expand-Archive -Path $dZip -DestinationPath (Join-Path $tmp 'deno') -Force
    Copy-Item (Join-Path $tmp 'deno\deno.exe') (Join-Path $bin 'deno.exe') -Force
    Ok ((& (Join-Path $bin 'deno.exe') --version | Select-Object -First 1))

    # ---- 4. WebView2 SDK: trinh duyet nhung de bat link video ----
    Say 'Dang tai WebView2 SDK...'
    $wvVer = '1.0.2792.45'
    $wvPkg = Join-Path $tmp 'wv2.zip'
    Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$wvVer/microsoft.web.webview2.$wvVer.nupkg" `
        -OutFile $wvPkg -UseBasicParsing
    Expand-Archive -Path $wvPkg -DestinationPath (Join-Path $tmp 'wv2') -Force
    foreach ($dll in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'Microsoft.Web.WebView2.Wpf.dll') {
        Copy-Item (Join-Path $tmp "wv2\lib\net462\$dll") (Join-Path $bin $dll) -Force
    }
    Copy-Item (Join-Path $tmp 'wv2\runtimes\win-x64\native\WebView2Loader.dll') (Join-Path $bin 'WebView2Loader.dll') -Force
    Ok 'WebView2 SDK'

    # ---- 5. Kiem tra WebView2 Runtime co san tren may chua ----
    $k = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($k) {
        Ok ('WebView2 Runtime ' + (Get-ItemProperty $k).pv)
    } else {
        Write-Host '  !!  Chua co WebView2 Runtime (chuc nang "Bat video tu trang web" se khong chay).' -ForegroundColor Yellow
        Write-Host '      Tai tai: https://developer.microsoft.com/microsoft-edge/webview2/' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host 'XONG! Chay TaiVideo.bat de mo app.' -ForegroundColor Green
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# =============================================================
#  Tải Video — cài đặt: tải engine + build app + tạo shortcut
#  Chạy 1 lần sau khi clone:
#     powershell -ExecutionPolicy Bypass -File setup.ps1
# =============================================================

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin  = Join-Path $root 'bin'
$app  = Join-Path $root 'app'
$proj = Join-Path $root 'src\SimpleVidDownload\SimpleVidDownload.csproj'
$tmp  = Join-Path $env:TEMP ('taivideo_setup_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $bin, $tmp -Force | Out-Null

function Say([string]$m) { Write-Host $m -ForegroundColor Cyan }
function Ok ([string]$m) { Write-Host ('  OK  ' + $m) -ForegroundColor Green }
function Warn([string]$m) { Write-Host ('  !!  ' + $m) -ForegroundColor Yellow }

try {
    # ---- 1. yt-dlp: engine tai video ----
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

    # ---- 4. Real-ESRGAN: nang do phan giai bang AI (tuy chon) ----
    Say 'Dang tai cong cu AI upscale (~45MB)...'
    try {
        $esZip = Join-Path $tmp 'esrgan.zip'
        Invoke-WebRequest -Uri 'https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip' `
            -OutFile $esZip -UseBasicParsing
        $esDir = Join-Path $bin 'realesrgan'
        New-Item -ItemType Directory -Path $esDir -Force | Out-Null
        Expand-Archive -Path $esZip -DestinationPath (Join-Path $tmp 'es') -Force
        foreach ($f in 'realesrgan-ncnn-vulkan.exe', 'vcomp140.dll') {
            Copy-Item (Join-Path $tmp "es\$f") (Join-Path $esDir $f) -Force
        }
        Copy-Item (Join-Path $tmp 'es\models') $esDir -Recurse -Force
        Ok 'Real-ESRGAN (app se tu kiem tra GPU co chay noi khong)'
    } catch {
        Warn 'Khong tai duoc Real-ESRGAN — app van chay binh thuong, chi la khong co muc nang cap AI.'
    }

    # ---- 5. Build app C# ----
    Say 'Dang build app...'
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Warn 'Chua co .NET SDK. Cai bang lenh:  winget install Microsoft.DotNet.SDK.10'
        Warn 'Hoac tai tai: https://dotnet.microsoft.com/download'
        throw 'Thieu .NET SDK'
    }
    & dotnet publish $proj -c Release -o $app --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build that bai' }
    Ok ('app\DCDownload.exe')

    # ---- 6. WebView2 Runtime (can cho che do bat video) ----
    $k = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($k) {
        Ok ('WebView2 Runtime ' + (Get-ItemProperty $k).pv)
    } else {
        Warn 'Chua co WebView2 Runtime (chuc nang "Bat video tu trang web" se khong chay).'
        Warn 'Tai tai: https://developer.microsoft.com/microsoft-edge/webview2/'
    }

    # ---- 7. Shortcut ngoai Desktop ----
    Say 'Dang tao shortcut...'
    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'DCDownload.lnk'))
    $lnk.TargetPath = Join-Path $app 'DCDownload.exe'
    $lnk.WorkingDirectory = $app
    $lnk.IconLocation = (Join-Path $app 'DCDownload.exe') + ',0'
    $lnk.Description = 'DCDownload - DragonCloud Download'
    $lnk.Save()
    Ok 'Shortcut "DCDownload" tren Desktop'

    Write-Host ''
    Write-Host 'XONG! Nhay dup shortcut "DCDownload" tren Desktop de mo app.' -ForegroundColor Green
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

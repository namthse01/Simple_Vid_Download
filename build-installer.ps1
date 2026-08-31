# =============================================================
#  Dong goi DCDownload-Setup.exe
#     powershell -ExecutionPolicy Bypass -File build-installer.ps1
#
#  Quy trinh: build app (kem san .NET runtime) -> nhung vao trinh cai
#  -> build trinh cai thanh MOT file exe duy nhat.
# =============================================================

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProj = Join-Path $root 'src\SimpleVidDownload\SimpleVidDownload.csproj'
$setProj = Join-Path $root 'src\Installer\DCDownloadSetup.csproj'
$payload = Join-Path $root 'src\Installer\payload'
$dist    = Join-Path $root 'dist'

function Say([string]$m) { Write-Host $m -ForegroundColor Cyan }
function Ok ([string]$m) { Write-Host ('  OK  ' + $m) -ForegroundColor Green }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Chua co .NET SDK. Cai bang: winget install Microsoft.DotNet.SDK.10'
}

# ---- 1. Build app: tu chua runtime, gom thanh 1 file ----
Say 'Build app (ban tu chua .NET runtime)...'
$appOut = Join-Path $env:TEMP ('dcd_app_' + [guid]::NewGuid().ToString('N').Substring(0,8))
& dotnet publish $appProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $appOut --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build app that bai' }

$appExe = Join-Path $appOut 'DCDownload.exe'
if (-not (Test-Path $appExe)) { throw 'Khong thay DCDownload.exe sau khi build' }
Ok ('DCDownload.exe  ' + [math]::Round((Get-Item $appExe).Length / 1MB, 1) + ' MB')

# ---- 2. Dat vao cho de trinh cai nhung vao ----
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item $appExe (Join-Path $payload 'DCDownload.exe') -Force
Remove-Item $appOut -Recurse -Force -ErrorAction SilentlyContinue

# ---- 3. Chep bo style sang trinh cai ----
# Phai chep THAT vao thu muc project: lien ket file XAML tu project khac
# khong duoc MSBuild dua vao assembly (app se loi 'Cannot locate resource').
Copy-Item (Join-Path $root 'src\SimpleVidDownload\Theme.xaml') `
          (Join-Path $root 'src\Installer\Theme.xaml') -Force

# ---- 4. Build trinh cai ----
Say 'Build trinh cai...'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
& dotnet publish $setProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $dist --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build trinh cai that bai' }

# don rac, chi giu lai file exe
Get-ChildItem $dist -File | Where-Object { $_.Extension -ne '.exe' } |
    Remove-Item -Force -ErrorAction SilentlyContinue

$setup = Join-Path $dist 'DCDownload-Setup.exe'
if (-not (Test-Path $setup)) { throw 'Khong thay DCDownload-Setup.exe' }

Write-Host ''
Ok ('dist\DCDownload-Setup.exe  ' + [math]::Round((Get-Item $setup).Length / 1MB, 1) + ' MB')
Write-Host ''
Write-Host 'XONG! Gui file dist\DCDownload-Setup.exe cho ai muon dung.' -ForegroundColor Green
Write-Host 'May nhan KHONG can cai .NET, Python hay gi khac.' -ForegroundColor Green

# Installs Matrix Rain into Windows so it shows up in the screen-saver list.
# Copies the .scr into the system folders (needs admin) and opens the
# Screen Saver Settings dialog. Run by right-clicking -> "Run with PowerShell".

$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'MatrixRain.scr'

if (-not (Test-Path $src)) {
    Write-Host "MatrixRain.scr not found next to this script." -ForegroundColor Red
    exit 1
}

# Re-launch elevated if we are not already admin (System32 needs admin).
$admin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent() `
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $admin) {
    Write-Host "Requesting administrator rights to install..." -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList `
        "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

Copy-Item $src "$env:windir\System32\MatrixRain.scr" -Force
if (Test-Path "$env:windir\SysWOW64") {
    Copy-Item $src "$env:windir\SysWOW64\MatrixRain.scr" -Force
}

Write-Host "Installed 'Matrix Rain' to the system screen-saver folder." -ForegroundColor Green
Write-Host "Opening Screen Saver Settings - pick 'MatrixRain' from the dropdown." -ForegroundColor Green

# Open the Screen Saver Settings dialog directly.
Start-Process rundll32.exe 'desk.cpl,InstallScreenSaver MatrixRain.scr'

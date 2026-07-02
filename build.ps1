# Builds MatrixRain.scr from source using the C# compiler that ships with Windows.
$ErrorActionPreference = 'Stop'
$csc = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:windir\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

& $csc /nologo /target:winexe /optimize+ `
    "/out:$PSScriptRoot\MatrixRain.scr" `
    /reference:System.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll `
    "$PSScriptRoot\MatrixRain.cs"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Built MatrixRain.scr" -ForegroundColor Green
} else {
    Write-Host "Build failed." -ForegroundColor Red
    exit 1
}

param([switch]$SkipAppBuild)

$ErrorActionPreference = 'Stop'

if (-not $SkipAppBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
}

$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
$applicationPath = Join-Path $PSScriptRoot 'dist\DonkeyNet.exe'
if (-not (Test-Path -LiteralPath $applicationPath)) {
    throw 'ابتدا فایل اجرایی برنامه را بسازید.'
}

$fileVersion = (Get-Item -LiteralPath $applicationPath).VersionInfo.FileVersion
if ($fileVersion -ne "$version.0") {
    throw "نسخهٔ فایل اجرایی ($fileVersion) با VERSION ($version) یکسان نیست."
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup 6 پیدا نشد. آن را با winget install JRSoftware.InnoSetup نصب کنید.'
}

$scriptPath = Join-Path $PSScriptRoot 'installer\DonkeyNet.iss'
& $compiler "/DMyAppVersion=$version" $scriptPath
if ($LASTEXITCODE -ne 0) { throw 'ساخت فایل Setup ناموفق بود.' }

$setupPath = Join-Path $PSScriptRoot 'dist\DonkeyNet-Setup.exe'
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash
$checksumPath = Join-Path $PSScriptRoot 'dist\DonkeyNet-Setup.exe.sha256'
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$hash  DonkeyNet-Setup.exe$([Environment]::NewLine)",
    (New-Object System.Text.UTF8Encoding($false))
)

$file = Get-Item -LiteralPath $setupPath
Write-Host "نصب‌ساز ساخته شد: $($file.FullName) — $([math]::Round($file.Length / 1KB, 1)) KB"

$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'کامپایلر داخلی ویندوز (.NET Framework csc.exe) پیدا نشد.'
}

$versionFile = Join-Path $PSScriptRoot 'VERSION'
if (-not (Test-Path -LiteralPath $versionFile)) {
    throw "فایل نسخه پیدا نشد: $versionFile"
}
$releaseVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
$sourceText = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'DonkeyNet.cs') -Raw
$expectedAssemblyVersion = '[assembly: AssemblyVersion("' + $releaseVersion + '.0")]'
if (-not $sourceText.Contains($expectedAssemblyVersion)) {
    throw "نسخهٔ VERSION با AssemblyVersion کد یکسان نیست: $releaseVersion"
}

$outputDirectory = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$iconSource = Join-Path $PSScriptRoot 'assets\donkey-icon.png'
$iconFile = Join-Path $outputDirectory 'DonkeyNet.ico'
if (-not (Test-Path -LiteralPath $iconSource)) {
    throw "تصویر آیکن پیدا نشد: $iconSource"
}

Add-Type -AssemblyName System.Drawing
$sourceImage = [System.Drawing.Image]::FromFile($iconSource)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = New-Object System.Collections.Generic.List[byte[]]
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($sourceImage, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
            }
            finally { $graphics.Dispose() }

            $stream = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($stream.ToArray())
            }
            finally { $stream.Dispose() }
        }
        finally { $bitmap.Dispose() }
    }
}
finally { $sourceImage.Dispose() }

$fileStream = [System.IO.File]::Open($iconFile, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($fileStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + (16 * $frames.Count)
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
}

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu `
    /reference:Microsoft.CSharp.dll `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Management.dll `
    /reference:System.Windows.Forms.dll `
    /win32icon:"$iconFile" `
    /out:"$outputDirectory\DonkeyNet.exe" `
    "$PSScriptRoot\DonkeyNet.cs"

if ($LASTEXITCODE -ne 0) { throw 'ساخت برنامه ناموفق بود.' }

$file = Get-Item -LiteralPath "$outputDirectory\DonkeyNet.exe"
$hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
$checksumPath = Join-Path $outputDirectory 'DonkeyNet.exe.sha256'
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$hash  DonkeyNet.exe$([Environment]::NewLine)",
    (New-Object System.Text.UTF8Encoding($false))
)
Write-Host "ساخته شد: $($file.FullName) — $([math]::Round($file.Length / 1KB, 1)) KB"

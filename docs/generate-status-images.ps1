$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$iconPath = Join-Path $root 'assets\donkey-icon.png'
$outputDirectory = Join-Path $PSScriptRoot 'images'
$sampleConnection = 'سوار کدوم خری؟ Redmi WiFi'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

function New-RoundedRectanglePath {
    param([System.Drawing.RectangleF]$Rectangle, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-StatusImage {
    param(
        [string]$FileName,
        [string]$Accent,
        [string]$Title,
        [string]$Connection,
        [string]$Ping,
        [string]$Description
    )

    $bitmap = New-Object System.Drawing.Bitmap(1200, 360, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $logo = [System.Drawing.Image]::FromFile($iconPath)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 13, 17, 23))

        $shadowPath = New-RoundedRectanglePath -Rectangle (New-Object System.Drawing.RectangleF(29, 31, 1142, 302)) -Radius 30
        $cardPath = New-RoundedRectanglePath -Rectangle (New-Object System.Drawing.RectangleF(24, 24, 1142, 302)) -Radius 30
        $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 0, 0, 0))
        $cardBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 31, 35, 43))
        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 66, 72, 82), 2)
        $accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($Accent))
        try {
            $graphics.FillPath($shadowBrush, $shadowPath)
            $graphics.FillPath($cardBrush, $cardPath)
            $graphics.DrawPath($borderPen, $cardPath)
            $graphics.FillRectangle($accentBrush, 24, 86, 8, 180)
            $graphics.DrawImage($logo, (New-Object System.Drawing.Rectangle(66, 119, 122, 122)))

            $nameFont = New-Object System.Drawing.Font('Segoe UI', 18, [System.Drawing.FontStyle]::Regular)
            $titleFont = New-Object System.Drawing.Font('Tahoma', 26, [System.Drawing.FontStyle]::Bold)
            $connectionFont = New-Object System.Drawing.Font('Tahoma', 18, [System.Drawing.FontStyle]::Regular)
            $pingFont = New-Object System.Drawing.Font('Tahoma', 20, [System.Drawing.FontStyle]::Regular)
            $descriptionFont = New-Object System.Drawing.Font('Tahoma', 18, [System.Drawing.FontStyle]::Regular)
            $mutedBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 185, 191, 201))
            $whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 245, 247, 250))
            $format = New-Object System.Drawing.StringFormat
            try {
                $format.Alignment = [System.Drawing.StringAlignment]::Far
                $format.LineAlignment = [System.Drawing.StringAlignment]::Center
                $format.FormatFlags = [System.Drawing.StringFormatFlags]::DirectionRightToLeft
                $graphics.DrawString('DonkeyNet.exe', $nameFont, $mutedBrush, 220, 58)
                $graphics.FillEllipse($accentBrush, 1090, 65, 18, 18)
                $graphics.DrawString($Title, $titleFont, $whiteBrush, (New-Object System.Drawing.RectangleF(230, 88, 880, 52)), $format)
                $graphics.DrawString($Connection, $connectionFont, $mutedBrush, (New-Object System.Drawing.RectangleF(230, 139, 880, 42)), $format)
                $graphics.DrawString($Ping, $pingFont, $mutedBrush, (New-Object System.Drawing.RectangleF(230, 180, 880, 44)), $format)
                $graphics.DrawString($Description, $descriptionFont, $whiteBrush, (New-Object System.Drawing.RectangleF(230, 226, 880, 56)), $format)
            }
            finally {
                $format.Dispose()
                $mutedBrush.Dispose()
                $whiteBrush.Dispose()
                $nameFont.Dispose()
                $titleFont.Dispose()
                $connectionFont.Dispose()
                $pingFont.Dispose()
                $descriptionFont.Dispose()
            }
        }
        finally {
            $shadowPath.Dispose()
            $cardPath.Dispose()
            $shadowBrush.Dispose()
            $cardBrush.Dispose()
            $borderPen.Dispose()
            $accentBrush.Dispose()
        }

        $outputPath = Join-Path $outputDirectory $FileName
        $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Created $outputPath"
    }
    finally {
        $logo.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-StatusImage -FileName 'status-good.png' -Accent '#22C55E' `
    -Title 'اینترنت خر نیست.' -Connection $sampleConnection -Ping 'سرعت خر: ۸۵ میلی‌ثانیه' `
    -Description 'اینترنت خوبه، بگو حمد و سپاس خدارا.'
New-StatusImage -FileName 'status-weak.png' -Accent '#FBBF24' `
    -Title 'اینترنت خر است.' -Connection $sampleConnection -Ping 'سرعت خر: ۲۲۰ میلی‌ثانیه' `
    -Description 'اینترنت ضعیفه، بازم جای شکرش باقیه.'
New-StatusImage -FileName 'status-very-weak.png' -Accent '#F97316' `
    -Title 'اینترنت خیلی خر است.' -Connection $sampleConnection -Ping 'سرعت خر: ۴۸۰ میلی‌ثانیه' `
    -Description 'اینترنت خیلی ضعیفه، اینجا ایرانه مشکل داری جمع کن برو.'
New-StatusImage -FileName 'status-offline.png' -Accent '#EF4444' `
    -Title 'اینترنت خود خر است.' -Connection $sampleConnection -Ping 'سرعت خر: قطع' `
    -Description 'اینترنتی وجود نداره، برو بمیر.'

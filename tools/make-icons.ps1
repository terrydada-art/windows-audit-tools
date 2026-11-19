# 產生兩個程式的 .ico (原創向量繪製，無第三方素材)。
# Generates the two application icons (original vector drawings, no third-party assets).
# 用法 / Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icons.ps1 [-PreviewDir <dir>]
param([string]$PreviewDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Tile($g, [int]$s, [string]$c1, [string]$c2) {
    $m = [Math]::Max(0.5, $s * 0.03)
    $rect = New-Object System.Drawing.RectangleF($m, $m, ($s - 2 * $m), ($s - 2 * $m))
    $path = New-RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.2)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.ColorTranslator]::FromHtml($c1), [System.Drawing.ColorTranslator]::FromHtml($c2), 90.0)
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

# 登入稽核：藍色底 + 白色盾牌 + 使用者剪影
function Draw-Login($g, [int]$s) {
    Draw-Tile $g $s '#2F7BD8' '#163F85'
    $f = [single]$s
    $shield = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shield.AddLine(($f * 0.50), ($f * 0.13), ($f * 0.81), ($f * 0.24))
    $shield.AddLine(($f * 0.81), ($f * 0.24), ($f * 0.81), ($f * 0.48))
    $shield.AddBezier(($f * 0.81), ($f * 0.48), ($f * 0.81), ($f * 0.70), ($f * 0.66), ($f * 0.82), ($f * 0.50), ($f * 0.89))
    $shield.AddBezier(($f * 0.50), ($f * 0.89), ($f * 0.34), ($f * 0.82), ($f * 0.19), ($f * 0.70), ($f * 0.19), ($f * 0.48))
    $shield.AddLine(($f * 0.19), ($f * 0.48), ($f * 0.19), ($f * 0.24))
    $shield.CloseFigure()
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $shield)

    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#1D4E9E'))
    $hr = $f * 0.095
    $g.FillEllipse($blue, ($f * 0.50 - $hr), ($f * 0.43 - $hr), ($hr * 2), ($hr * 2))
    $old = $g.Clip
    $g.SetClip($shield)
    $g.FillEllipse($blue, ($f * 0.31), ($f * 0.575), ($f * 0.38), ($f * 0.36))
    $g.Clip = $old
    $white.Dispose(); $blue.Dispose(); $shield.Dispose()
}

# 電源事件：綠色底 + 白色電源符號
function Draw-Power($g, [int]$s) {
    Draw-Tile $g $s '#2BB07F' '#12684A'
    $f = [single]$s
    $w = [Math]::Max(1.6, $f * 0.10)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $w)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $r = $f * 0.27
    $cx = $f * 0.50; $cy = $f * 0.55
    $g.DrawArc($pen, ($cx - $r), ($cy - $r), ($r * 2), ($r * 2), -50, 280)
    $g.DrawLine($pen, $cx, ($f * 0.19), $cx, ($f * 0.50))
    $pen.Dispose()
}

function Write-Ico([string]$path, [scriptblock]$draw, [int[]]$frameSizes = $sizes) {
    $pngs = @()
    foreach ($s in $frameSizes) {
        $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        & $draw $g $s
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        if ($PreviewDir -and $s -in 32, 256) { $bmp.Save((Join-Path $PreviewDir ("{0}-{1}.png" -f [IO.Path]::GetFileNameWithoutExtension((Split-Path (Split-Path $path) -Leaf)), $s))) }
        $bmp.Dispose()
        $pngs += , @($s, $ms.ToArray())
    }

    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($out)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $dim = if ($p[0] -ge 256) { 0 } else { $p[0] }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$p[1].Length); $bw.Write([uint32]$offset)
        $offset += $p[1].Length
    }
    foreach ($p in $pngs) { $bw.Write([byte[]]$p[1]) }
    $bw.Flush()
    [IO.File]::WriteAllBytes($path, $out.ToArray())
    "{0}  {1:N0} bytes" -f $path, $out.Length
}

Write-Ico (Join-Path $root 'LoginAuditor\app.ico') ${function:Draw-Login}
Write-Ico (Join-Path $root 'PowerEventViewer\app.ico') ${function:Draw-Power}

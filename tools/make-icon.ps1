# SunTool 아이콘 생성 스크립트
# 둥근 사각형(주황→호박색 그라데이션) 위에 흰색 굵은 "S" 한 글자.
# 실행: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
# 결과: src\SunTool.App\Assets\SunTool.ico (16~256px), docs\manual-src\images\icon-256.png (미리보기)
param(
    [string]$OutIco = (Join-Path $PSScriptRoot '..\src\SunTool.App\Assets\SunTool.ico'),
    [string]$OutPng = (Join-Path $PSScriptRoot '..\src\SunTool.App\Assets\SunTool-256.png')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$top    = [System.Drawing.Color]::FromArgb(255, 255, 170, 40)   # 밝은 호박색
$bottom = [System.Drawing.Color]::FromArgb(255, 240, 100, 20)   # 진한 주황

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

function Render([int]$size) {
    # 작은 크기는 여백을 줄이고 글자를 상대적으로 크게 해 뭉개지지 않게 함
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = if ($size -le 16) { 0 } elseif ($size -le 32) { [Math]::Max(1, $size * 0.03) } else { $size * 0.06 }
    $box = $size - 2 * $pad
    $bg = New-RoundRect $pad $pad $box $box ($box * 0.22)
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, $box, $box)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $bg)

    # 글자: 외곽선 경로로 그린 뒤 실제 외곽 크기에 맞춰 정중앙 배치
    $family = New-Object System.Drawing.FontFamily($familyName)
    $glyph = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glyph.AddString('S', $family, [int][System.Drawing.FontStyle]::Bold, 100, (New-Object System.Drawing.PointF(0, 0)), [System.Drawing.StringFormat]::GenericTypographic)
    $b = $glyph.GetBounds()
    $target = $box * $(if ($size -le 16) { 0.80 } elseif ($size -le 32) { 0.74 } else { 0.66 })
    $scale = $target / [Math]::Max($b.Width, $b.Height)
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate($size / 2, $size / 2)
    $m.Scale($scale, $scale)
    $m.Translate(-($b.X + $b.Width / 2), -($b.Y + $b.Height / 2))
    $glyph.Transform($m)

    if ($size -ge 48) {
        # 큰 크기에서만 은은한 그림자
        $shadow = $glyph.Clone()
        $sm = New-Object System.Drawing.Drawing2D.Matrix
        $sm.Translate(0, $size * 0.015)
        $shadow.Transform($sm)
        $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(60, 120, 40, 0))), $shadow)
    }
    $g.FillPath([System.Drawing.Brushes]::White, $glyph)

    $g.Dispose()
    return $bmp
}

$installed = (New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name }
$familyName = @('Segoe UI Black', 'Arial Black', 'Segoe UI') | Where-Object { $installed -contains $_ } | Select-Object -First 1

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        # 256 미만은 호환성을 위해 전통적인 32비트 DIB 프레임 (BITMAPINFOHEADER + BGRA 하향식 역순 + AND 마스크)
        $bw = New-Object System.IO.BinaryWriter($ms)
        $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([uint32]($s * $s * 4 + $maskStride * $s))
        $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $s)))   # 알파 채널을 쓰므로 마스크는 모두 0
        $bw.Flush()
    }
    if ($s -eq 256) {
        New-Item -ItemType Directory -Force (Split-Path $OutPng) | Out-Null
        $bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Data = $ms.ToArray() }
}

# ICO 파일: 헤더 + 디렉터리 + PNG 프레임 (Windows Vista 이후 모든 크기 PNG 프레임 지원)
New-Item -ItemType Directory -Force (Split-Path $OutIco) | Out-Null
$fs = [System.IO.File]::Create($OutIco)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Data.Length); $w.Write([uint32]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $w.Write($f.Data) }
$w.Close()

Write-Host "아이콘 생성: $((Resolve-Path $OutIco).Path) ($($frames.Count)개 크기, 글꼴 $familyName)"

#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SourceSvg,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $SourceSvg) { $SourceSvg = Join-Path $repository 'src/MpvShell.App/Assets/Branding/Veyra.svg' }
if (-not $OutputDirectory) { $OutputDirectory = Split-Path -Parent $SourceSvg }
$SourceSvg = (Resolve-Path -LiteralPath $SourceSvg).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

# 母版只包含实色多边形，SVG 与位图使用相同坐标。
# 遇到不支持的 SVG 特性直接报错，避免静默遗漏图形。
[xml] $svg = [IO.File]::ReadAllText($SourceSvg)
$root = $svg.DocumentElement
if ($root.LocalName -ne 'svg' -or $root.GetAttribute('viewBox') -ne '0 0 256 256') {
    throw 'SVG 母版必须使用 256×256 坐标范围。'
}
$polygons = @($root.ChildNodes | Where-Object NodeType -eq Element)
if (@($polygons | Where-Object LocalName -notin @('title', 'desc', 'polygon')).Count -gt 0) {
    throw '此生成器仅支持 title、desc 和实色 polygon 元素。'
}
$polygons = @($polygons | Where-Object LocalName -eq 'polygon')
if ($polygons.Count -eq 0) { throw 'SVG 母版中没有多边形。' }

Add-Type -AssemblyName System.Drawing
$shapes = foreach ($polygon in $polygons) {
    if ($polygon.GetAttribute('fill') -notmatch '^#[0-9A-Fa-f]{6}$') { throw '多边形填充必须为不透明的 RGB 颜色。' }
    if ($polygon.Attributes | Where-Object Name -notin @('fill', 'points')) { throw '多边形包含不支持的属性。' }
    $points = foreach ($pair in ($polygon.GetAttribute('points').Trim() -split '\s+')) {
        $coordinates = $pair -split ','
        if ($coordinates.Count -ne 2) { throw "顶点坐标无效：$pair" }
        $x = [float]::Parse($coordinates[0], [Globalization.CultureInfo]::InvariantCulture)
        $y = [float]::Parse($coordinates[1], [Globalization.CultureInfo]::InvariantCulture)
        [Drawing.PointF]::new($x, $y)
    }
    if ($points.Count -lt 3) { throw '多边形至少需要三个顶点。' }
    [pscustomobject]@{ Points = [Drawing.PointF[]] $points; Color = [Drawing.ColorTranslator]::FromHtml($polygon.GetAttribute('fill')) }
}

function Render-Png([int] $Size) {
    # 每个目标尺寸独立以四倍分辨率渲染，不通过放大小图生成。
    $supersampled = [Drawing.Bitmap]::new($Size * 4, $Size * 4, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $drawing = $null
    $output = $null
    $stream = [IO.MemoryStream]::new()
    try {
        $drawing = [Drawing.Graphics]::FromImage($supersampled)
        $drawing.Clear([Drawing.Color]::Transparent)
        $drawing.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $drawing.ScaleTransform($Size * 4 / 256.0, $Size * 4 / 256.0)
        foreach ($shape in $shapes) {
            $brush = [Drawing.SolidBrush]::new($shape.Color)
            try { $drawing.FillPolygon($brush, $shape.Points) } finally { $brush.Dispose() }
        }
        $output = [Drawing.Graphics]::FromImage($bitmap)
        $output.Clear([Drawing.Color]::Transparent)
        $output.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $output.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $output.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $output.DrawImage($supersampled, [Drawing.Rectangle]::new(0, 0, $Size, $Size), 0, 0, $supersampled.Width, $supersampled.Height, [Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        if ($output) { $output.Dispose() }
        if ($drawing) { $drawing.Dispose() }
        $stream.Dispose()
        $bitmap.Dispose()
        $supersampled.Dispose()
    }
}

[IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'Veyra.png'), (Render-Png 512))

# ICO 图层使用 PNG 数据，项目目标 Windows 版本支持此格式。
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) { [pscustomobject]@{ Size = $size; Png = Render-Png $size } }
$iconStream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] $frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte] $dimension)
        $writer.Write([byte] $dimension)
        $writer.Write([byte] 0)
        $writer.Write([byte] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] 32)
        $writer.Write([uint32] $frame.Png.Length)
        $writer.Write([uint32] $offset)
        $offset += $frame.Png.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]] $frame.Png) }
    $writer.Flush()
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'Veyra.ico'), $iconStream.ToArray())
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
}
Write-Output "已从 $SourceSvg 生成 Veyra.png（512×512 RGBA）和 Veyra.ico（$($sizes -join ', ') 像素）。"

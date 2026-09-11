#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $FfmpegPath = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\tools\ffmpeg-8.1.2-essentials_build\bin\ffmpeg.exe'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\media'),
    [switch] $Regenerate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactPrefix = (Join-Path $repository 'artifacts').TrimEnd('\') + '\'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $destination.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw '测试素材只能写入本仓库 artifacts 子目录。'
}
if (-not (Test-Path -LiteralPath $FfmpegPath -PathType Leaf)) {
    throw "未找到 FFmpeg。请传入 -FfmpegPath，固定工具下载与校验见 docs/implementation/hdr-4k-progress.md：$FfmpegPath"
}
$ffmpeg = (Resolve-Path -LiteralPath $FfmpegPath).Path
$ffprobe = Join-Path ([IO.Path]::GetDirectoryName($ffmpeg)) 'ffprobe.exe'
if (-not (Test-Path -LiteralPath $ffprobe -PathType Leaf)) { throw "缺少同目录 ffprobe.exe：$ffprobe" }
[void] [IO.Directory]::CreateDirectory($destination)
$manifestPath = Join-Path $destination 'media-manifest.json'
if ((Test-Path -LiteralPath $manifestPath) -and -not $Regenerate) {
    & (Join-Path $PSScriptRoot 'verify-hdr-4k-media.ps1') -MediaDirectory $destination -FfprobePath $ffprobe -FfmpegPath $ffmpeg
    return
}

if (-not ('HdrTestPattern' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'HdrTestPattern.cs') }
$sourcePath = Join-Path $destination 'pq-pattern-3840x2160-yuv420p10le.yuv'
[HdrTestPattern]::WriteRaw($sourcePath)
$sourceHash = (Get-FileHash -LiteralPath $sourcePath).Hash
$patches = @([HdrTestPattern]::PatchNits | ForEach-Object {
    [ordered]@{ intendedNits = $_; limitedRange10BitCode = [HdrTestPattern]::NitsToCode($_) }
})
$pqOptions = @('-color_range', 'tv', '-color_primaries', 'bt2020', '-color_trc', 'smpte2084', '-colorspace', 'bt2020nc')
$pqFrameTags = 'setparams=range=limited:color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc'
$rawInput = @('-stream_loop', '-1', '-f', 'rawvideo', '-pixel_format', 'yuv420p10le', '-video_size', '3840x2160')
$common = @('-hide_banner', '-nostdin', '-y', '-filter_threads', '2', '-filter_complex_threads', '2')
$samples = [Collections.Generic.List[object]]::new()

function Encode-Sample([string] $Name, [string[]] $Arguments, [hashtable] $Expected, [string] $Purpose) {
    $path = Join-Path $destination $Name
    $commandArguments = $common + $Arguments + @('-map_metadata', '-1', '-fflags', '+bitexact', $path)
    $log = Join-Path $destination ($Name + '.encode.log')
    Write-Host "CPU 合成：$Name"
    & $ffmpeg @commandArguments 2>&1 | Out-File -LiteralPath $log -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "编码失败（退出码 $LASTEXITCODE）：$log" }
    $samples.Add([ordered]@{
        fileName = $Name; purpose = $Purpose; bytes = (Get-Item -LiteralPath $path).Length
        sha256 = (Get-FileHash -LiteralPath $path).Hash; expected = $Expected; ffmpegArguments = $commandArguments
    })
}

# 高光按 PQ 公式生成，绝不把 SDR 画面仅重新贴上 HDR 标签。
$x265 = 'pools=2:frame-threads=1:lookahead-threads=1:hdr10=1:repeat-headers=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:range=limited:master-display=G(8500,39850)B(6550,2300)R(35400,14600)WP(15635,16450)L(10000000,1):max-cll=1000,1000'
Encode-Sample 'hevc-main10-hdr10-4k30.mkv' ($rawInput + @('-framerate', '30', '-i', $sourcePath, '-t', '8',
    '-vf', ($pqFrameTags + ',scroll=horizontal=0.002'), '-an', '-c:v', 'libx265', '-preset', 'ultrafast', '-crf', '18',
    '-pix_fmt', 'yuv420p10le', '-x265-params', $x265) + $pqOptions) `
    @{ codecName = 'hevc'; profile = 'Main 10'; pixelFormat = 'yuv420p10le'; transfer = 'smpte2084'; primaries = 'bt2020'; matrix = 'bt2020nc'; frameRate = '30/1'; hdr10Metadata = $true } `
    '4K30 HEVC Main10、HDR10 静态元数据与横向运动；目标高光 1000 nits。此合成图案不代替高复杂度电影的性能验收。'

# FFV1 保留源图案每个 10-bit 码值，用于渐变/暗部检查，不用它判断硬件解码。
Encode-Sample 'pq-gradient-10bit-4k.mkv' ($rawInput + @('-framerate', '1', '-i', $sourcePath, '-t', '8',
    '-vf', $pqFrameTags, '-an', '-c:v', 'ffv1', '-level', '3', '-coder', '1', '-context', '1', '-g', '1', '-threads', '2', '-pix_fmt', 'yuv420p10le') + $pqOptions) `
    @{ codecName = 'ffv1'; pixelFormat = 'yuv420p10le'; transfer = 'smpte2084'; primaries = 'bt2020'; matrix = 'bt2020nc'; frameRate = '1/1'; hdr10Metadata = $false; decodedFirstFrameSha256 = $sourceHash } `
    '无损 10-bit PQ 灰阶：上半部 0–1000 nits 等 PQ 码值渐变，中段 0–5 nits 暗部渐变，下部 11 档亮度块；软件解码。'

Encode-Sample 'av1-main10-sdr-4k30.mkv' @('-f', 'lavfi', '-i', 'testsrc2=size=3840x2160:rate=30:duration=8',
    '-vf', 'setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709',
    '-an', '-c:v', 'libaom-av1', '-usage', 'realtime', '-cpu-used', '8', '-row-mt', '1', '-tiles', '2x2',
    '-threads', '2', '-lag-in-frames', '0', '-crf', '38', '-b:v', '0', '-pix_fmt', 'yuv420p10le',
    '-color_range', 'tv', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709') `
    @{ codecName = 'av1'; profile = 'Main'; pixelFormat = 'yuv420p10le'; transfer = 'bt709'; primaries = 'bt709'; matrix = 'bt709'; frameRate = '30/1'; hdr10Metadata = $false } `
    '4K30 AV1 Main 10-bit SDR 动态图案；支持 AV1 硬解的机器记录实际硬解，否则检查明确的软件回退。'

$version = (& $ffmpeg -version | Select-Object -First 1)
$manifest = [ordered]@{
    schemaVersion = 1; generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    provenance = '全部在本机合成，不含用户媒体。生成器与图案允许随本项目复制、修改和再分发。'
    width = 3840; height = 2160; durationSeconds = 8
    tool = [ordered]@{ ffmpeg = $ffmpeg; version = $version; ffmpegSha256 = (Get-FileHash -LiteralPath $ffmpeg).Hash; ffprobeSha256 = (Get-FileHash -LiteralPath $ffprobe).Hash }
    pqPattern = [ordered]@{ sourceFile = [IO.Path]::GetFileName($sourcePath); sha256 = $sourceHash; patchOrderLeftToRight = $patches; topRampUniqueCodeCount = 660; range = 'limited (64–940)'; chromaCode = 512 }
    samples = $samples.ToArray()
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
& (Join-Path $PSScriptRoot 'verify-hdr-4k-media.ps1') -MediaDirectory $destination -FfprobePath $ffprobe -FfmpegPath $ffmpeg -Decode

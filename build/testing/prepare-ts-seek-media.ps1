#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $FfmpegPath = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\tools\ffmpeg-8.1.2-essentials_build\bin\ffmpeg.exe'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\media'),
    [switch] $Regenerate
)

# 生成一份开放 GOP 的 HEVC MPEG-TS 合成样片，用于 TsSeekDecodePathComparisonTests：
# 1280×720 @ 60 fps，关键帧间隔 60 帧（1 秒），带 B 帧与 RASL 前导帧，附 AAC 正弦音轨，30 秒。
# libavformat 的 MPEG-TS 没有关键帧索引，底层 seek 会落在 GOP 中间，正好复现 LG 演示片的跳转问题。

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
[void] [IO.Directory]::CreateDirectory($destination)
$name = 'hevc-opengop-seek-720p60.ts'
$path = Join-Path $destination $name
$manifestPath = Join-Path $destination ($name + '.manifest.json')
if ((Test-Path -LiteralPath $path) -and (Test-Path -LiteralPath $manifestPath) -and -not $Regenerate) {
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $actual = (Get-FileHash -LiteralPath $path).Hash
    if ($actual -ne $manifest.sha256) { throw "样片哈希与清单不符：$path" }
    Write-Host "样片已存在且哈希一致：$path"
    return
}

$arguments = @(
    '-hide_banner', '-nostdin', '-y', '-loglevel', 'error',
    '-f', 'lavfi', '-i', 'testsrc2=size=1280x720:rate=60:duration=30',
    '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:duration=30',
    '-map', '0:v', '-map', '1:a',
    '-c:v', 'libx265', '-preset', 'veryfast', '-crf', '24', '-pix_fmt', 'yuv420p',
    '-x265-params', 'keyint=60:min-keyint=60:open-gop=1:bframes=4:b-adapt=0:rc-lookahead=20:repeat-headers=1:log-level=error',
    '-c:a', 'aac', '-b:a', '96k',
    '-f', 'mpegts', '-map_metadata', '-1', '-fflags', '+bitexact', $path
)
Write-Host "CPU 合成：$name"
& $ffmpeg @arguments
if ($LASTEXITCODE -ne 0) { throw "编码失败（退出码 $LASTEXITCODE）" }

$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    provenance = '本机合成，不含用户媒体；testsrc2 图案与正弦音允许随本项目复制、修改和再分发。'
    fileName = $name
    bytes = (Get-Item -LiteralPath $path).Length
    sha256 = (Get-FileHash -LiteralPath $path).Hash
    ffmpeg = $ffmpeg
    ffmpegSha256 = (Get-FileHash -LiteralPath $ffmpeg).Hash
    ffmpegArguments = $arguments
    expected = [ordered]@{ codec = 'hevc'; width = 1280; height = 720; fps = 60; gopFrames = 60; openGop = $true; durationSeconds = 30 }
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Host "已写入 $manifestPath"

#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $FfmpegPath = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\tools\ffmpeg-8.1.2-essentials_build\bin\ffmpeg.exe'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\p1-04\media'),
    [switch] $Regenerate
)

# 固定的本机 VOD、多轨和字幕验收输入，不含用户媒体，不在运行测试时联网下载或编码。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactPrefix = (Join-Path $repository 'artifacts').TrimEnd('\') + '\'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $destination.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw '测试素材只能写入本仓库 artifacts 子目录。'
}
$ffmpeg = (Resolve-Path -LiteralPath $FfmpegPath).Path
$ffprobe = Join-Path (Split-Path $ffmpeg) 'ffprobe.exe'
if (-not (Test-Path -LiteralPath $ffprobe -PathType Leaf)) { throw '需要同目录的 ffprobe.exe 核验流信息。' }
$manifestPath = Join-Path $destination 'media-manifest.json'
if ((Test-Path -LiteralPath $manifestPath) -and -not $Regenerate) {
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) { throw '不支持的素材清单版本。' }
    foreach ($file in $manifest.files) {
        $path = Join-Path $destination $file.name
        if ((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "素材哈希不符：$path" }
    }
    Write-Host "P1-04 素材已存在且哈希一致：$destination"
    return
}
[void][IO.Directory]::CreateDirectory((Join-Path $destination 'hls'))
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $destination 'english.srt'), "1`n00:00:00,000 --> 00:00:12,000`nP1-04 SRT SUBTITLE`n", $utf8)
$ass = @'
[Script Info]
ScriptType: v4.00+
PlayResX: 320
PlayResY: 180
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,24,&H0000FFFF,&H0000FFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,8,10,10,12,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.00,0:00:12.00,Default,,0,0,0,,P1-04 ASS SUBTITLE
'@
[IO.File]::WriteAllText((Join-Path $destination 'styled.ass'), $ass.Replace("`r`n", "`n") + "`n", $utf8)
$commands = [Collections.Generic.List[object]]::new()
function Invoke-Encoding([string[]] $Arguments) {
    $commands.Add($Arguments)
    & $ffmpeg @Arguments
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg 编码失败：$LASTEXITCODE" }
}
$common = @('-hide_banner', '-nostdin', '-y', '-loglevel', 'error')
Invoke-Encoding ($common + @(
    '-f', 'lavfi', '-i', 'testsrc2=size=320x180:rate=24:duration=12',
    '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:duration=12',
    '-map', '0:v', '-map', '1:a', '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '20',
    '-pix_fmt', 'yuv420p', '-g', '48', '-keyint_min', '48', '-sc_threshold', '0', '-bf', '0',
    '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709',
    '-c:a', 'aac', '-b:a', '64k', '-metadata:s:a:0', 'language=eng',
    '-f', 'hls', '-hls_time', '2', '-hls_list_size', '0', '-hls_playlist_type', 'vod',
    '-hls_flags', 'independent_segments', '-hls_segment_filename', (Join-Path $destination 'hls/part%02d.ts'),
    (Join-Path $destination 'hls/index.m3u8')
))
Invoke-Encoding ($common + @(
    '-i', (Join-Path $destination 'hls/index.m3u8'),
    '-f', 'lavfi', '-i', 'sine=frequency=880:sample_rate=44100:duration=12',
    '-i', (Join-Path $destination 'english.srt'), '-i', (Join-Path $destination 'styled.ass'),
    '-map', '0:v', '-map', '0:a', '-map', '1:a', '-map', '2:s', '-map', '3:s',
    '-c:v', 'copy', '-c:a:0', 'copy', '-c:a:1', 'pcm_s16le', '-c:s:0', 'srt', '-c:s:1', 'ass',
    '-metadata:s:a:0', 'language=eng', '-metadata:s:a:0', 'title=English 440 Hz',
    '-metadata:s:a:1', 'language=zho', '-metadata:s:a:1', 'title=中文 880 Hz',
    '-metadata:s:s:0', 'language=eng', '-metadata:s:s:0', 'title=English SRT',
    '-metadata:s:s:1', 'language=zho', '-metadata:s:s:1', 'title=ASS 样式字幕',
    '-disposition:a:0', 'default', '-disposition:a:1', '0',
    '-disposition:s:0', '0', '-disposition:s:1', '0', (Join-Path $destination 'multitrack.mkv')
))
Invoke-Encoding ($common + @(
    '-f', 'lavfi', '-i', 'sine=frequency=220:sample_rate=32000:duration=4',
    '-c:a', 'pcm_s16le', (Join-Path $destination 'audio-only.wav')
))
Invoke-Encoding ($common + @(
    '-f', 'lavfi', '-i', 'color=c=blue:size=160x90:rate=24:duration=4',
    '-c:v', 'libx264', '-preset', 'veryfast', '-pix_fmt', 'yuv420p', '-an',
    '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709',
    (Join-Path $destination 'video-only.mkv')
))
$probes = [ordered]@{}
foreach ($name in @('hls/index.m3u8', 'multitrack.mkv', 'audio-only.wav', 'video-only.mkv')) {
    $output = & $ffprobe -v error -show_streams -show_format -of json (Join-Path $destination $name)
    if ($LASTEXITCODE -ne 0) { throw "无法读取媒体流：$name" }
    $probes[$name] = ($output -join "`n") | ConvertFrom-Json
}
$multi = $probes['multitrack.mkv'].streams
if (@($multi | Where-Object codec_type -eq video).Count -ne 1 -or
    @($multi | Where-Object codec_type -eq audio).Count -ne 2 -or
    @($multi | Where-Object codec_type -eq subtitle).Count -ne 2) { throw '多轨素材的流数量错误。' }
if ((@($multi | Where-Object codec_type -eq audio).codec_name -join ',') -ne 'aac,pcm_s16le' -or
    (@($multi | Where-Object codec_type -eq subtitle).codec_name -join ',') -ne 'subrip,ass') { throw '音频或字幕编码不符合验收输入。' }
$video = $probes['hls/index.m3u8'].streams | Where-Object codec_type -eq video
if ($video.codec_name -ne 'h264' -or $video.width -ne 320 -or $video.height -ne 180 -or
    $video.avg_frame_rate -ne '24/1') { throw '视频 HLS 的编码、尺寸或帧率错误。' }
if (@(Get-ChildItem -LiteralPath (Join-Path $destination 'hls') -Filter 'part*.ts').Count -ne 6) { throw 'HLS 应包含六个 2 秒分片。' }
$files = @(Get-ChildItem -LiteralPath $destination -File -Recurse | Where-Object Name -ne 'media-manifest.json' | Sort-Object FullName | ForEach-Object {
    [ordered]@{ name = [IO.Path]::GetRelativePath($destination, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
[ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    provenance = '本机 lavfi 图案、正弦音和项目自写字幕；无用户媒体。字体依赖测试机 Arial，未附带或再分发字体。'
    ffmpegVersion = (& $ffmpeg -version | Select-Object -First 1)
    ffmpegSha256 = (Get-FileHash -LiteralPath $ffmpeg).Hash
    ffprobeSha256 = (Get-FileHash -LiteralPath $ffprobe).Hash
    commands = $commands.ToArray()
    expected = [ordered]@{ width = 320; height = 180; fps = 24; seconds = 12; hlsSegments = 6; subtitleFormats = @('subrip', 'ass'); audioCodecs = @('aac', 'pcm_s16le') }
    files = $files
    probes = $probes
} | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Host "已生成并核验 P1-04 素材：$manifestPath"

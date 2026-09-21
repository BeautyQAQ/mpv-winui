#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $FfmpegPath = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\tools\ffmpeg-8.1.2-essentials_build\bin\ffmpeg.exe'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\media'),
    [switch] $Regenerate
)

# P1-01 性能场景的固定 4K60 素材。私人 LG 4K HDR 演示片不能进入 Git，这里用可再生成的合成样片承接
# 同一场景（4K60 HEVC Main10 HDR10 → SDR 色调映射 / PQ 输出），并给出 SDR 8-bit 对照以分离色调映射开销。
# 合成图案的码率与复杂度低于真实电影，只能证明链路吞吐，不能代替高码率片源；结论范围写入证据文档。

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
$manifestPath = Join-Path $destination 'perf-media-manifest.json'

$common = @('-hide_banner', '-nostdin', '-y', '-loglevel', 'error')
$pqOptions = @('-color_range', 'tv', '-color_primaries', 'bt2020', '-color_trc', 'smpte2084', '-colorspace', 'bt2020nc')
$pqFrameTags = 'setparams=range=limited:color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc'
$x265Hdr = 'pools=4:frame-threads=2:hdr10=1:repeat-headers=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:range=limited:master-display=G(8500,39850)B(6550,2300)R(35400,14600)WP(15635,16450)L(10000000,1):max-cll=1000,1000:keyint=120:min-keyint=60'
$x265Sdr = 'pools=4:frame-threads=2:repeat-headers=1:keyint=120:min-keyint=60'

$samples = @(
    [ordered]@{
        fileName = 'hevc-main10-hdr10-4k60-30s.mkv'
        purpose = '4K60 HEVC Main10、HDR10 静态元数据、30 秒；PERF-01 的固定承接素材：SDR 色调映射与 PQ 输出场景共用。'
        arguments = @('-f', 'lavfi', '-i', 'testsrc2=size=3840x2160:rate=60:duration=30',
            '-vf', ('format=yuv420p10le,' + $pqFrameTags), '-an', '-c:v', 'libx265', '-preset', 'ultrafast', '-crf', '20',
            '-pix_fmt', 'yuv420p10le', '-x265-params', $x265Hdr) + $pqOptions
        expected = [ordered]@{ codecName = 'hevc'; profile = 'Main 10'; pixelFormat = 'yuv420p10le'; transfer = 'smpte2084'; primaries = 'bt2020'; frameRate = '60/1'; durationSeconds = 30 }
        note = 'testsrc2 图案按 PQ/BT.2020 标记并写入 HDR10 元数据，用于驱动同一色调映射着色器路径；像素值不是按 PQ 公式生成的真实亮度，不用于亮度/色彩准确性判断。'
    },
    [ordered]@{
        fileName = 'hevc-main-sdr-4k60-30s.mkv'
        purpose = '4K60 HEVC Main 8-bit SDR、30 秒；与 HDR10 样片同图案，用于分离“4K60 解码 + 呈现”与“色调映射”的开销。'
        arguments = @('-f', 'lavfi', '-i', 'testsrc2=size=3840x2160:rate=60:duration=30',
            '-vf', 'format=yuv420p,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709',
            '-an', '-c:v', 'libx265', '-preset', 'ultrafast', '-crf', '20', '-pix_fmt', 'yuv420p', '-x265-params', $x265Sdr,
            '-color_range', 'tv', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709')
        expected = [ordered]@{ codecName = 'hevc'; profile = 'Main'; pixelFormat = 'yuv420p'; transfer = 'bt709'; primaries = 'bt709'; frameRate = '60/1'; durationSeconds = 30 }
        note = 'SDR 对照；与 HDR10 样片使用相同图案、帧率、时长与 GOP。'
    }
)

if ((Test-Path -LiteralPath $manifestPath) -and -not $Regenerate) {
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    foreach ($entry in $manifest.samples) {
        $path = Join-Path $destination $entry.fileName
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "清单登记的样片不存在：$path（可加 -Regenerate 重新生成）" }
        $actual = (Get-FileHash -LiteralPath $path).Hash
        if ($actual -ne $entry.sha256) { throw "样片哈希与清单不符：$path" }
        Write-Host "样片已存在且哈希一致：$($entry.fileName)"
    }
    return
}

$results = [Collections.Generic.List[object]]::new()
foreach ($sample in $samples) {
    $path = Join-Path $destination $sample.fileName
    $log = Join-Path $destination ($sample.fileName + '.encode.log')
    $commandArguments = $common + $sample.arguments + @('-map_metadata', '-1', '-fflags', '+bitexact', $path)
    Write-Host "CPU 合成（4K60，30 秒，可能需要数分钟）：$($sample.fileName)"
    & $ffmpeg @commandArguments 2>&1 | Out-File -LiteralPath $log -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "编码失败（退出码 $LASTEXITCODE）：$log" }
    $results.Add([ordered]@{
        fileName = $sample.fileName
        purpose = $sample.purpose
        note = $sample.note
        bytes = (Get-Item -LiteralPath $path).Length
        sha256 = (Get-FileHash -LiteralPath $path).Hash
        expected = $sample.expected
        ffmpegArguments = $commandArguments
    })
}

$version = (& $ffmpeg -version | Select-Object -First 1)
$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    provenance = '全部在本机合成，不含用户媒体；testsrc2 图案允许随本项目复制、修改和再分发。'
    tool = [ordered]@{ ffmpeg = $ffmpeg; version = $version; ffmpegSha256 = (Get-FileHash -LiteralPath $ffmpeg).Hash }
    samples = $results.ToArray()
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Host "已写入 $manifestPath"

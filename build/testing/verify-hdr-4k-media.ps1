#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $MediaDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\media'),
    [string] $FfprobePath = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\tools\ffmpeg-8.1.2-essentials_build\bin\ffprobe.exe'),
    [string] $FfmpegPath = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\tools\ffmpeg-8.1.2-essentials_build\bin\ffmpeg.exe'),
    [switch] $Decode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $MediaDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $directory 'media-manifest.json') -Raw | ConvertFrom-Json -AsHashtable
$results = [Collections.Generic.List[object]]::new()
function Read-Rational([string] $Value) {
    $parts = $Value.Split('/')
    return [double]::Parse($parts[0], [Globalization.CultureInfo]::InvariantCulture) / [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
}
foreach ($sample in $manifest.samples) {
    if ([IO.Path]::GetFileName($sample.fileName) -ne $sample.fileName) { throw '清单媒体名不能包含目录。' }
    $path = Join-Path $directory $sample.fileName
    $actualHash = (Get-FileHash -LiteralPath $path).Hash
    if ($actualHash -ne $sample.sha256) { throw "素材 SHA-256 不符：$path" }

    $probeJson = & $FfprobePath -v error -select_streams v:0 -show_streams -show_format -of json $path
    if ($LASTEXITCODE -ne 0) { throw "读取素材元数据失败：$path" }
    $probe = ($probeJson -join "`n") | ConvertFrom-Json -AsHashtable
    $stream = $probe.streams[0]
    $expected = $sample.expected
    $checks = [ordered]@{
        codec_name = $expected.codecName; width = 3840; height = 2160
        pix_fmt = $expected.pixelFormat; color_transfer = $expected.transfer
        color_primaries = $expected.primaries; color_space = $expected.matrix
        color_range = 'tv'; r_frame_rate = $expected.frameRate
    }
    if ($expected.ContainsKey('profile')) { $checks.profile = $expected.profile }
    foreach ($key in $checks.Keys) {
        if ($stream[$key] -ne $checks[$key]) { throw "素材 $($sample.fileName) 的 $key 不符：实际 $($stream[$key])，预期 $($checks[$key])。" }
    }
    $duration = [double]::Parse($probe.format.duration, [Globalization.CultureInfo]::InvariantCulture)
    if ([Math]::Abs($duration - $manifest.durationSeconds) -gt 0.1) { throw "素材时长不符：$duration" }

    $firstFrame = $null
    if ($expected.hdr10Metadata) {
        $frameJson = & $FfprobePath -v error -select_streams v:0 -read_intervals '%+#1' -show_frames -of json $path
        if ($LASTEXITCODE -ne 0) { throw "读取 HDR10 首帧元数据失败：$path" }
        $firstFrame = ($frameJson -join "`n") | ConvertFrom-Json -AsHashtable
        $sideData = @($firstFrame.frames[0].side_data_list)
        $types = @($sideData | ForEach-Object { $_.side_data_type })
        if ('Mastering display metadata' -notin $types -or 'Content light level metadata' -notin $types) {
            throw "缺少 HDR10 mastering display 或 content light SEI：$path"
        }
        $mastering = @($sideData | Where-Object { $_.side_data_type -eq 'Mastering display metadata' })[0]
        $contentLight = @($sideData | Where-Object { $_.side_data_type -eq 'Content light level metadata' })[0]
        if ((Read-Rational $mastering.max_luminance) -ne 1000 -or (Read-Rational $mastering.min_luminance) -ne 0.0001 `
            -or $contentLight.max_content -ne 1000 -or $contentLight.max_average -ne 1000) {
            throw "HDR10 亮度 SEI 与生成约定不符：$path"
        }
    }

    $decodedFirstFrameMatches = $null
    $gradientUniqueCodes = $null
    if ($Decode) {
        # 只进行 CPU 解码和文件验证，不启动 App，不请求 GPU/hwaccel。
        & $FfmpegPath -hide_banner -nostdin -v error -xerror -threads 2 -i $path -map 0:v:0 -an -f null NUL
        if ($LASTEXITCODE -ne 0) { throw "完整 CPU 解码失败：$path" }
        if ($expected.ContainsKey('decodedFirstFrameSha256')) {
            $decoded = Join-Path $directory ($sample.fileName + '.first-frame.yuv')
            & $FfmpegPath -hide_banner -nostdin -v error -y -threads 2 -i $path -frames:v 1 -pix_fmt yuv420p10le -f rawvideo $decoded
            if ($LASTEXITCODE -ne 0) { throw "渐变首帧解码失败：$path" }
            $decodedFirstFrameMatches = (Get-FileHash -LiteralPath $decoded).Hash -eq $expected.decodedFirstFrameSha256
            if (-not $decodedFirstFrameMatches) { throw '无损渐变解码后的像素与原始 10-bit 图案不同。' }
            $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($decoded))
            try {
                $codes = [Collections.Generic.HashSet[UInt16]]::new()
                for ($pixel = 0; $pixel -lt 3840; $pixel++) { [void] $codes.Add($reader.ReadUInt16()) }
                $gradientUniqueCodes = $codes.Count
                if ($gradientUniqueCodes -ne $manifest.pqPattern.topRampUniqueCodeCount -or $gradientUniqueCodes -le 256) {
                    throw "渐变的不同码值不足以证明 10-bit 输入：$gradientUniqueCodes"
                }
            }
            finally { $reader.Dispose() }
        }
    }
    $probe | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $directory ($sample.fileName + '.ffprobe.json')) -Encoding utf8NoBOM
    if ($null -ne $firstFrame) {
        $firstFrame | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $directory ($sample.fileName + '.first-frame.json')) -Encoding utf8NoBOM
    }
    $results.Add([ordered]@{
        fileName = $sample.fileName; sha256 = $actualHash; metadataPassed = $true
        fullCpuDecodePassed = $(if ($Decode) { $true } else { $null }); decodedFirstFrameMatches = $decodedFirstFrameMatches
        gradientUniqueCodes = $gradientUniqueCodes
        codec = $stream.codec_name; pixelFormat = $stream.pix_fmt; width = $stream.width; height = $stream.height
        frameRate = $stream.r_frame_rate; transfer = $stream.color_transfer; durationSeconds = $duration
    })
}
$report = [ordered]@{
    verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); gpuOrApplicationTestPerformed = $false
    samples = $results.ToArray()
}
$reportFile = if ($Decode) { 'media-verification.json' } else { 'media-metadata-verification.json' }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $directory $reportFile) -Encoding utf8NoBOM
$results.ToArray() | ForEach-Object { [pscustomobject] $_ } | Format-Table fileName, codec, pixelFormat, frameRate, metadataPassed, fullCpuDecodePassed

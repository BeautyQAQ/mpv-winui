#Requires -Version 7.0
<#
PERF-02：在同一构建/设备下依次运行 S4、S5 的有采样/无采样对照。
只使用离屏 Composition SwapChain，不代表可见窗口、HDR 输出或长期稳定性验收。
每组默认三轮，交替顺序，保留所有原始结果；性能未达标不会伪装成测试失败或自动归因。
#>
[CmdletBinding()]
param(
    [string] $MediaDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\hdr-4k\media'),
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug',
    [ValidateSet('Auto', 'Off')][string] $D3D11DebugLayer = 'Auto',
    [ValidateRange(1, 10)][int] $Repetitions = 3,
    [string] $ReportDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\p3-01'),
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$mediaRoot = (Resolve-Path -LiteralPath $MediaDirectory).Path
$reportRoot = [IO.Path]::GetFullPath($ReportDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not ($reportRoot + [IO.Path]::DirectorySeparatorChar).StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw '报告目录必须位于本仓库 artifacts 下。'
}
$runId = '{0}-{1}-{2}' -f $Configuration, [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $reportRoot $runId
[void](New-Item -ItemType Directory -Path $runRoot)

function Write-Json([object] $Value, [string] $Path) {
    $Value | ConvertTo-Json -Depth 24 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

function Get-Identity([string[]] $Paths) {
    @($Paths | Sort-Object -Unique | ForEach-Object {
        $file = Get-Item -LiteralPath $_
        [ordered]@{ path = $file.FullName; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    })
}

function Test-Identity([object[]] $Identity) {
    $changes = @($Identity | Where-Object {
        -not (Test-Path -LiteralPath $_.path -PathType Leaf) -or (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256
    })
    return $changes.Count -eq 0
}

function Read-TestResults([string] $Directory) {
    $total = 0; $passed = 0; $failed = 0; $notExecuted = 0
    $targetPassed = 0
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -File -Recurse)
    foreach ($file in $files) {
        [xml] $trx = Get-Content -LiteralPath $file.FullName -Raw
        $counters = $trx.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $counters) { throw "TRX 缺少 Counters：$($file.FullName)" }
        $total += [int] $counters.total; $passed += [int] $counters.passed
        $failed += [int] $counters.failed; $notExecuted += [int] $counters.notExecuted
        foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
            if ($result.testName -eq $targetTestName -and $result.outcome -eq 'Passed') { $targetPassed++ }
        }
    }
    [ordered]@{ files = @($files | ForEach-Object FullName); total = $total; passed = $passed; failed = $failed; notExecuted = $notExecuted; targetPassed = $targetPassed }
}

function Read-Measurement([string] $Path, [object] $Run, [string] $ExpectedHash, [string] $MpvHash) {
    $report = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    $scenario = $report.Scenario; $steady = $report.Steady; $statistics = $steady.Statistics
    $problems = [Collections.Generic.List[string]]::new()
    if ($report.Status -ne '已测量，未判定' -or $null -ne $report.Error) { $problems.Add('原始报告失败或未完成') }
    if ($report.MediaSha256 -ne $ExpectedHash -or $report.MpvSha256 -ne $MpvHash) { $problems.Add('素材或 libmpv 哈希不符') }
    if ($scenario.BuildConfiguration -ne $Configuration -or $scenario.Output -ne 'sdr' -or
        $scenario.SurfaceWidth -ne 3840 -or $scenario.SurfaceHeight -ne 2160 -or
        $scenario.WarmupSeconds -ne 2 -or $scenario.SteadySeconds -ne 5 -or
        $scenario.TargetFps -ne 30 -or $scenario.SyncInterval -ne 1 -or
        $scenario.Hwdec -ne 'd3d11va' -or $scenario.RequireHardwareDecode -ne ($Run.scenario -eq 'S4') -or
        $null -ne $scenario.MinimumPresentedFps -or $scenario.ReadbackIntervalSeconds -ne $Run.readbackIntervalSeconds) {
        $problems.Add('实际场景与固定参数不符')
    }
    if ($null -eq $report.Device -or $report.Device.IsWarpDevice -or
        $report.Device.OutputFormat -ne 'B8G8R8A8_UNorm' -or
        $report.Device.SurfaceWidth -ne 3840 -or $report.Device.SurfaceHeight -ne 2160) { $problems.Add('实际设备/输出不符合场景') }
    if ($D3D11DebugLayer -eq 'Off' -and $report.Device.D3D11DebugLayerEnabled) { $problems.Add('调试层未按要求关闭') }
    if ($steady.EndedAtEof -or $steady.PositionAdvancedSeconds -lt 4.5 -or -not $report.NonBlackPixelObserved) { $problems.Add('有效稳态或非黑画面不足') }
    if ($null -eq $steady.WallSeconds -or -not [double]::IsFinite([double] $steady.WallSeconds) -or $steady.WallSeconds -le 0 -or
        $null -eq $statistics.ElapsedSeconds -or -not [double]::IsFinite([double] $statistics.ElapsedSeconds) -or $statistics.ElapsedSeconds -le 0) { $problems.Add('测量墙钟或统计时窗无效') }
    if (($Run.sampling -eq 'sampled' -and $steady.ReadbackSamples -le 0) -or
        ($Run.sampling -eq 'unsampled' -and $steady.ReadbackSamples -ne 0)) { $problems.Add('实际读回次数与分组不符') }
    $decode = $report.Info.DecodeDiagnostics
    if ($null -eq $decode -or $decode.Mode -eq 'Unknown') { $problems.Add('解码模式未知') }
    if ($Run.scenario -eq 'S4' -and -not $decode.IsGpuResident) { $problems.Add('S4 未实现 GPU 驻留硬解') }
    # S5 沿用产品 d3d11va 请求，仅允许回退；支持 AV1 硬解的机器不能冒充软件回退场景。
    if ($Run.scenario -eq 'S5' -and $decode.Mode -ne 'Software') { $problems.Add('S5 本机实际并非软件回退，不能套用该场景结论') }
    if ($steady.CountersAtStart.HwdecCurrent -ne $steady.CountersAtEnd.HwdecCurrent) { $problems.Add('窗口内解码方式变化') }
    $fps = $statistics.PresentedFramesPerSecond
    if ($null -eq $fps -or -not [double]::IsFinite([double] $fps) -or $statistics.PresentedFrames -le 0 -or
        $null -eq $steady.VoDroppedFrames -or $null -eq $steady.DecoderDroppedFrames -or
        $steady.VoDroppedFrames -lt 0 -or $steady.DecoderDroppedFrames -lt 0) { $problems.Add('帧率或丢帧计数缺失/无效') }
    # 将既有 R1 的“窗口帧数”明确按实际呈现帧数计算，不用四舍五入放宽阈值。
    $maximumVoDrops = [double] $statistics.PresentedFrames * 0.01
    $r1Passed = $problems.Count -eq 0 -and $fps -ge 29.4 -and $steady.VoDroppedFrames -le $maximumVoDrops -and $steady.DecoderDroppedFrames -eq 0
    [ordered]@{
        originalStatus = $report.Status; valid = $problems.Count -eq 0; problems = $problems.ToArray(); r1Passed = $r1Passed
        presentedFps = $fps; presentedFrames = $statistics.PresentedFrames; maximumVoDroppedFrames = $maximumVoDrops
        voDroppedFrames = $steady.VoDroppedFrames; decoderDroppedFrames = $steady.DecoderDroppedFrames
        wallSeconds = $steady.WallSeconds; statisticsElapsedSeconds = $statistics.ElapsedSeconds
        positionStartSeconds = $steady.PositionStartSeconds; positionEndSeconds = $steady.PositionEndSeconds
        positionAdvancedSeconds = $steady.PositionAdvancedSeconds; readbackSamples = $steady.ReadbackSamples
        actualReadbacksPerSecond = $(if ($steady.WallSeconds -gt 0) { $steady.ReadbackSamples / $steady.WallSeconds } else { $null })
        render = $statistics.Render; present = $statistics.Present; wakeToPresent = $statistics.WakeToPresent
        device = $report.Device; decodeMode = $decode.Mode; decoder = $decode.Decoder; isGpuResident = $decode.IsGpuResident
        comparisonIdentity = [ordered]@{
            os = $report.OperatingSystem; device = $report.Device; mediaSha256 = $report.MediaSha256; mpvSha256 = $report.MpvSha256
            configuration = $scenario.BuildConfiguration; hwdecAtStart = $steady.CountersAtStart.HwdecCurrent
            hwdecAtEnd = $steady.CountersAtEnd.HwdecCurrent; decodeMode = $decode.Mode; decoder = $decode.Decoder
        }
    }
}

$savedEnvironment = @{}
$environmentNames = @('MPVSHELL_D3D11_DEBUG_LAYER', 'MPVSHELL_TEST_HARDWARE_REPORT_DIR', 'MPVSHELL_TEST_NATIVE_LOG_LEVEL',
    'MPVSHELL_TEST_PERF_MEDIA', 'MPVSHELL_TEST_PERF_OUTPUT', 'MPVSHELL_TEST_PERF_PEAK_NITS', 'MPVSHELL_TEST_PERF_SURFACE',
    'MPVSHELL_TEST_PERF_WARMUP_SECONDS', 'MPVSHELL_TEST_PERF_SECONDS', 'MPVSHELL_TEST_PERF_HWDEC',
    'MPVSHELL_TEST_PERF_REQUIRE_HWDEC', 'MPVSHELL_TEST_PERF_SYNC_INTERVAL', 'MPVSHELL_TEST_PERF_READBACK_INTERVAL_SECONDS',
    'MPVSHELL_TEST_PERF_MIN_FPS', 'MPVSHELL_TEST_PERF_TARGET_FPS', 'MPVSHELL_TEST_PERF_LABEL')
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$runs = [Collections.Generic.List[object]]::new()
$pairs = [Collections.Generic.List[object]]::new()
$targetTestName = 'MpvShell.Rendering.WinUI.Tests.PlaybackPerformanceTests.Natural_playback_throughput_should_be_measured_and_reported_separately_from_functional_checks'
$summary = [ordered]@{
    schemaVersion = 1; startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); configuration = $Configuration
    requestedD3D11DebugLayer = $D3D11DebugLayer; repetitions = $Repetitions; reportDirectory = $runRoot; status = '执行中'
    scope = 'S4/S5 本机离屏对照；未绑定可见 visual；不替代应用窗口、物理 HDR、其他硬件和长期验收。'
    rule = 'R1：呈现 fps >= 29.4，VO 丢帧 <= 实际呈现帧数 * 0.01，解码丢帧 = 0；缺失计数、失败或不可比不得判通过。'
    readbackTimingLimit = '现有报告未单独计时 CPU 读回；Render 含读回。配对 Render 差值不是纯读回耗时，也不单独证明因果。'
    runs = $runs; pairs = $pairs; inputsUnchanged = $null; error = $null
}
$identity = @()
Push-Location $repository
try {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    if ($D3D11DebugLayer -eq 'Off') { $env:MPVSHELL_D3D11_DEBUG_LAYER = '0' }
    $manifestPath = Join-Path $mediaRoot 'media-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $scenarios = @(
        [ordered]@{ id = 'S4'; fileName = 'hevc-main10-hdr10-4k30.mkv'; requireHardware = '1' },
        [ordered]@{ id = 'S5'; fileName = 'av1-main10-sdr-4k30.mkv'; requireHardware = '0' }
    )
    foreach ($scenario in $scenarios) {
        $samples = @($manifest.samples | Where-Object fileName -eq $scenario.fileName)
        if ($samples.Count -ne 1 -or $samples[0].expected.frameRate -ne '30/1' -or $manifest.durationSeconds -ne 8 -or
            $manifest.width -ne 3840 -or $manifest.height -ne 2160) { throw "素材清单不符合 S4/S5：$($scenario.fileName)" }
        $scenario.path = (Resolve-Path -LiteralPath (Join-Path $mediaRoot $scenario.fileName)).Path
        $scenario.sha256 = (Get-FileHash -LiteralPath $scenario.path -Algorithm SHA256).Hash
        if ($scenario.sha256 -ne $samples[0].sha256) { throw "素材哈希不匹配：$($scenario.path)" }
    }
    if (-not $NoBuild) {
        & dotnet build mpv-winui.slnx -c $Configuration -p:Platform=x64 -v:minimal *> (Join-Path $runRoot 'build.log')
        if ($LASTEXITCODE -ne 0) { throw '解决方案构建失败；详见 build.log。' }
    }
    $binaryRoot = Join-Path $repository "tests\MpvShell.Rendering.WinUI.Tests\bin\x64\$Configuration\net10.0-windows10.0.19041.0"
    $testAssembly = Join-Path $binaryRoot 'MpvShell.Rendering.WinUI.Tests.dll'
    if (-not (Test-Path -LiteralPath $testAssembly -PathType Leaf)) { throw "未找到测试二进制：$testAssembly" }
    $binaryPaths = @(Get-ChildItem -LiteralPath $binaryRoot -Recurse -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -match '\.(deps|runtimeconfig)\.json$' } | Select-Object -ExpandProperty FullName)
    $identity = @(Get-Identity ($binaryPaths + @($manifestPath, $PSCommandPath) + @($scenarios.path)))
    $mpvHash = (Get-FileHash -LiteralPath (Join-Path $binaryRoot 'runtimes\win-x64\native\libmpv-2.dll')).Hash
    Write-Json ([ordered]@{ gitCommit = (& git rev-parse HEAD); gitStatus = @(& git status --porcelain); inputs = $identity }) (Join-Path $runRoot 'input-identity.json')
    $env:MPVSHELL_TEST_PERF_OUTPUT = 'sdr'; $env:MPVSHELL_TEST_PERF_PEAK_NITS = '1000'; $env:MPVSHELL_TEST_PERF_SURFACE = '3840x2160'
    $env:MPVSHELL_TEST_PERF_WARMUP_SECONDS = '2'; $env:MPVSHELL_TEST_PERF_SECONDS = '5'; $env:MPVSHELL_TEST_PERF_HWDEC = 'd3d11va'
    $env:MPVSHELL_TEST_PERF_SYNC_INTERVAL = '1'; $env:MPVSHELL_TEST_PERF_TARGET_FPS = '30'; $env:MPVSHELL_TEST_NATIVE_LOG_LEVEL = 'info'
    Write-Json $summary (Join-Path $runRoot 'comparison-summary.json')
    foreach ($scenario in $scenarios) {
        $env:MPVSHELL_TEST_PERF_MEDIA = $scenario.path; $env:MPVSHELL_TEST_PERF_REQUIRE_HWDEC = $scenario.requireHardware
        for ($iteration = 1; $iteration -le $Repetitions; $iteration++) {
            $order = if ($iteration % 2 -eq 1) { @('sampled', 'unsampled') } else { @('unsampled', 'sampled') }
            $currentPair = @{}
            foreach ($sampling in $order) {
                $label = '{0}-{1}-r{2}-{3}' -f $scenario.id, $Configuration.ToLowerInvariant(), $iteration, $sampling
                $directory = Join-Path $runRoot $label
                [void](New-Item -ItemType Directory -Path $directory)
                $env:MPVSHELL_TEST_PERF_LABEL = $label; $env:MPVSHELL_TEST_HARDWARE_REPORT_DIR = $directory
                $env:MPVSHELL_TEST_PERF_READBACK_INTERVAL_SECONDS = if ($sampling -eq 'sampled') { '0.1' } else { '0' }
                $arguments = @('test', 'mpv-winui.slnx', '-c', $Configuration, '-p:Platform=x64', '-m:1', '--no-build', '--no-restore',
                    '--filter', "FullyQualifiedName=$targetTestName", '--logger', 'trx', '--results-directory', $directory)
                $run = [ordered]@{
                    scenario = $scenario.id; iteration = $iteration; sampling = $sampling; label = $label
                    readbackIntervalSeconds = [double]::Parse($env:MPVSHELL_TEST_PERF_READBACK_INTERVAL_SECONDS, [Globalization.CultureInfo]::InvariantCulture)
                    startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); directory = $directory; exitCode = $null
                    measurement = $null; trx = $null; status = '执行中'; error = $null
                }
                Write-Json ([ordered]@{ executable = 'dotnet'; arguments = $arguments; environment = @($environmentNames | ForEach-Object { [ordered]@{ name = $_; value = [Environment]::GetEnvironmentVariable($_, 'Process') } }) }) (Join-Path $directory 'command.json')
                Write-Host "执行 $label"
                & dotnet @arguments *> (Join-Path $directory 'dotnet-test.log')
                $run.exitCode = $LASTEXITCODE
                try {
                    $run.trx = Read-TestResults $directory
                    $reportPath = Join-Path $directory "performance-$label.json"
                    if (Test-Path -LiteralPath $reportPath) { $run.measurement = Read-Measurement $reportPath $run $scenario.sha256 $mpvHash }
                    if ($run.exitCode -ne 0 -or $run.trx.total -ne 1 -or $run.trx.passed -ne 1 -or $run.trx.targetPassed -ne 1 -or $run.trx.failed -ne 0 -or $run.trx.notExecuted -ne 0 -or $null -eq $run.measurement) {
                        $run.status = '测试失败或未执行'
                    } elseif (-not $run.measurement.valid) { $run.status = '待判定（输入或场景不符）' }
                    elseif ($run.measurement.r1Passed) { $run.status = 'R1 通过' }
                    else { $run.status = 'R1 未达标，待分类' }
                } catch { $run.status = '结果解析失败'; $run.error = $_.Exception.Message }
                $runs.Add($run); $currentPair[$sampling] = $run
                Write-Json $summary (Join-Path $runRoot 'comparison-summary.json')
                # 禁止在对照期间重建或替换输入；改变后保留此前证据并终止，避免混用来源。
                if (-not (Test-Identity $identity)) { throw '测量期间输入/二进制发生变化，停止后续运行。' }
            }
            $sampled = $currentPair.sampled; $unsampled = $currentPair.unsampled
            $comparable = $null -ne $sampled.measurement -and $null -ne $unsampled.measurement -and
                $sampled.measurement.valid -and $unsampled.measurement.valid -and
                $sampled.status -in 'R1 通过', 'R1 未达标，待分类' -and $unsampled.status -in 'R1 通过', 'R1 未达标，待分类' -and
                ($sampled.measurement.comparisonIdentity | ConvertTo-Json -Depth 10 -Compress) -ceq ($unsampled.measurement.comparisonIdentity | ConvertTo-Json -Depth 10 -Compress)
            $pair = [ordered]@{ scenario = $scenario.id; iteration = $iteration; order = $order; comparable = $comparable
                sampledLabel = $sampled.label; unsampledLabel = $unsampled.label; status = '待判定'; fpsDeltaSampledMinusUnsampled = $null
                renderMeanDeltaMilliseconds = $null; renderP95DeltaMilliseconds = $null }
            if ($comparable) {
                $pair.fpsDeltaSampledMinusUnsampled = $sampled.measurement.presentedFps - $unsampled.measurement.presentedFps
                $pair.renderMeanDeltaMilliseconds = $sampled.measurement.render.AverageMilliseconds - $unsampled.measurement.render.AverageMilliseconds
                $pair.renderP95DeltaMilliseconds = $sampled.measurement.render.P95Milliseconds - $unsampled.measurement.render.P95Milliseconds
                if ($sampled.measurement.r1Passed -and $unsampled.measurement.r1Passed) { $pair.status = '两组 R1 通过' }
                elseif ($unsampled.measurement.r1Passed -and -not $sampled.measurement.r1Passed) { $pair.status = '有采样未达标、无采样通过；待复核 R2(b)' }
                else { $pair.status = '无采样未达标；待 R2(a)/(c) 对照分类' }
            }
            $pairs.Add($pair)
            Write-Json $summary (Join-Path $runRoot 'comparison-summary.json')
        }
    }
    $summary.status = if (@($pairs | Where-Object status -ne '两组 R1 通过').Count -eq 0) { '本机离屏配对 R1 通过；历史丢帧归因仍需复核' } else { '已完成测量，存在待判定/未达标项' }
} catch {
    $summary.status = '执行失败'; $summary.error = $_.Exception.Message
} finally {
    try {
        try {
            if ($identity.Count -gt 0) {
                $summary.inputsUnchanged = Test-Identity $identity
                if (-not $summary.inputsUnchanged) { $summary.status = '执行失败'; $summary.error = '测量输入或二进制发生变化，结果不得判通过。' }
            }
        }
        catch { $summary.inputsUnchanged = $false; $summary.error = $_.Exception.Message; $summary.status = '执行失败' }
        $summary.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Write-Json $summary (Join-Path $runRoot 'comparison-summary.json')
    } finally {
        foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
        Pop-Location
    }
}
Write-Host "$($summary.status)：$(Join-Path $runRoot 'comparison-summary.json')"
if ($summary.status -eq '执行失败' -or @($runs | Where-Object status -in '测试失败或未执行', '结果解析失败').Count -gt 0 -or $summary.inputsUnchanged -ne $true) { exit 1 }
# 退出 0 仅说明测量编排完成；性能结论必须读取汇总状态，不能用退出码关闭 PERF-02。

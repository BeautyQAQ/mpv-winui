#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $MediaPath,
    [string] $ReportDirectory,
    [ValidateNotNullOrEmpty()]
    [ValidateSet('playing', 'paused', 'immediate-failure', 'rebuild-failure', 'resize', 'playback', 'performance',
        'page-rebuild-playing', 'page-rebuild-paused', 'close-during-recovery', 'close-during-load', 'recovery-media-switch')]
    [string[]] $Mode = @('playing', 'paused', 'immediate-failure', 'rebuild-failure', 'resize',
        'page-rebuild-playing', 'page-rebuild-paused', 'close-during-recovery', 'close-during-load', 'recovery-media-switch'),
    [string] $AppPath,
    [switch] $NoBuild,
    # 以下仅影响 performance 模式：预热秒数、稳态窗口秒数。
    [ValidateRange(0, 600)] [double] $WarmupSeconds = 3,
    [ValidateRange(1, 600)] [double] $SteadySeconds = 30,
    # Auto：Debug 应用按默认行为启用 D3D11 调试层（若已安装 Graphics Tools）；Off：通过 MPVSHELL_D3D11_DEBUG_LAYER=0 关闭，
    # 用于在同一 Debug 构建下对照调试层开销。Release 应用不含探针，此开关对其无意义。
    [ValidateSet('Auto', 'Off')] [string] $D3D11DebugLayer = 'Auto',
    # 单个应用进程的限时；performance 模式会按预热与窗口自动放宽。
    [ValidateRange(30, 3600)] [int] $TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactDirectory = Join-Path $repository 'artifacts\app-recovery'
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N')
if ([string]::IsNullOrWhiteSpace($ReportDirectory)) {
    $ReportDirectory = Join-Path $artifactDirectory $runId
}
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
[void] [IO.Directory]::CreateDirectory($ReportDirectory)

function Write-RecoverySample([string] $Path, [switch] $SwapColors) {
    # BT.601 limited-range YUV420: red upper half, blue lower half（-SwapColors 交换上下颜色）。
    # Preserve the main colors; a moving white marker also exposes frozen playback.
    $topLuma = if ($SwapColors) { 41 } else { 81 }
    $bottomLuma = if ($SwapColors) { 81 } else { 41 }
    $topU = if ($SwapColors) { 240 } else { 90 }
    $bottomU = if ($SwapColors) { 90 } else { 240 }
    $topV = if ($SwapColors) { 110 } else { 240 }
    $bottomV = if ($SwapColors) { 240 } else { 110 }
    $baseFrame = [byte[]]::new(64 * 64 * 3 / 2)
    for ($row = 0; $row -lt 64; $row++) {
        for ($column = 0; $column -lt 64; $column++) {
            $baseFrame[$row * 64 + $column] = if ($row -lt 32) { $topLuma } else { $bottomLuma }
        }
    }
    for ($row = 0; $row -lt 32; $row++) {
        for ($column = 0; $column -lt 32; $column++) {
            $baseFrame[4096 + $row * 32 + $column] = if ($row -lt 16) { $topU } else { $bottomU }
            $baseFrame[5120 + $row * 32 + $column] = if ($row -lt 16) { $topV } else { $bottomV }
        }
    }
    $header = [Text.Encoding]::ASCII.GetBytes("YUV4MPEG2 W64 H64 F30:1 Ip A1:1 C420jpeg`n")
    $frameHeader = [Text.Encoding]::ASCII.GetBytes("FRAME`n")
    $frame = [byte[]]::new($baseFrame.Length)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $stream.Write($header, 0, $header.Length)
        for ($index = 0; $index -lt 30 * 20; $index++) {
            [Array]::Copy($baseFrame, $frame, $baseFrame.Length)
            # Even coordinates align the 8x16 marker with its YUV420 chroma cells.
            # The 58-frame period keeps captures 0.5 seconds apart visibly distinct.
            $markerX = [int] (2 * ([Math]::Floor($index / 2) % 29))
            for ($row = 8; $row -lt 24; $row++) {
                for ($column = $markerX; $column -lt $markerX + 8; $column++) {
                    $frame[$row * 64 + $column] = 235
                }
            }
            for ($row = 4; $row -lt 12; $row++) {
                for ($column = $markerX / 2; $column -lt $markerX / 2 + 4; $column++) {
                    $frame[4096 + $row * 32 + $column] = 128
                    $frame[5120 + $row * 32 + $column] = 128
                }
            }
            $stream.Write($frameHeader, 0, $frameHeader.Length)
            $stream.Write($frame, 0, $frame.Length)
        }
    }
    finally { $stream.Dispose() }
}

$generatedMedia = [string]::IsNullOrWhiteSpace($MediaPath)
if ($generatedMedia) {
    [void] [IO.Directory]::CreateDirectory($artifactDirectory)
    $MediaPath = Join-Path $artifactDirectory ('red-blue-' + $runId + '.y4m')
    Write-RecoverySample $MediaPath
}
if (-not (Test-Path -LiteralPath $MediaPath -PathType Leaf)) {
    throw "找不到测试视频：$MediaPath"
}
$MediaPath = (Resolve-Path -LiteralPath $MediaPath).Path

# 恢复中换媒体场景需要第二份不同颜色的样片；与主素材独立生成。
$switchMediaPath = $null
if ($Mode -contains 'recovery-media-switch') {
    [void] [IO.Directory]::CreateDirectory($artifactDirectory)
    $switchMediaPath = Join-Path $artifactDirectory ('red-blue-switch-' + $runId + '.y4m')
    Write-RecoverySample $switchMediaPath -SwapColors
}

if (-not $NoBuild) {
    $project = Join-Path $repository 'src\MpvShell.App\MpvShell.App.csproj'
    & dotnet build $project -c Debug -p:Platform=x64 -r win-x64
    if ($LASTEXITCODE -ne 0) { throw "Debug 应用构建失败，退出码 $LASTEXITCODE。" }
}
if ([string]::IsNullOrWhiteSpace($AppPath)) {
    $AppPath = Join-Path $repository 'src\MpvShell.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\MpvShell.App.exe'
}
if (-not (Test-Path -LiteralPath $AppPath -PathType Leaf)) {
    throw "找不到 Debug 应用：$AppPath。使用 -AppPath 指定已构建的 Debug 版本。"
}
$AppPath = (Resolve-Path -LiteralPath $AppPath).Path
$results = [Collections.Generic.List[object]]::new()
$previousDebugLayer = $env:MPVSHELL_D3D11_DEBUG_LAYER
if ($D3D11DebugLayer -eq 'Off') { $env:MPVSHELL_D3D11_DEBUG_LAYER = '0' }
try {
    foreach ($scenario in $Mode) {
        $reportPath = Join-Path $ReportDirectory ($scenario + '-' + [Guid]::NewGuid().ToString('N') + '.json')
        $process = $null
        $exitCode = $null
        $failure = $null
        $reportStatus = $null
        $shutdownCompleted = $null
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $limitSeconds = $TimeoutSeconds
        if ($scenario -eq 'performance') {
            $limitSeconds = [Math]::Max($TimeoutSeconds, [int][Math]::Ceiling($WarmupSeconds + $SteadySeconds + 60))
        }
        $loadListener = $null
        $scenarioLogDirectory = Join-Path $ReportDirectory ('logs-' + $scenario + '-' + [Guid]::NewGuid().ToString('N'))
        [void] [IO.Directory]::CreateDirectory($scenarioLogDirectory)
        $previousLogDirectory = $env:MPVSHELL_LOG_DIRECTORY
        $env:MPVSHELL_LOG_DIRECTORY = $scenarioLogDirectory
        try {
            # Start-Process joins ArgumentList on Windows, so quote entire path-bearing arguments.
            $arguments = [Collections.Generic.List[string]]::new()
            if ($scenario -ne 'close-during-load') { $arguments.Add(('"{0}"' -f $MediaPath)) }
            $arguments.Add(('"--recovery-test-report={0}"' -f $reportPath))
            $arguments.Add(('--recovery-test-mode={0}' -f $scenario))
            if ($generatedMedia -and $scenario -ne 'close-during-load') { $arguments.Add('--recovery-test-animated-pattern') }
            if ($scenario -eq 'recovery-media-switch') { $arguments.Add(('"--recovery-test-switch-media={0}"' -f $switchMediaPath)) }
            if ($scenario -eq 'close-during-load') {
                # 只接受连接、不响应请求的本机监听端口：加载真实停留在进行中，直到应用关闭。
                $loadListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
                $loadListener.Start()
                $loadUrl = 'http://127.0.0.1:{0}/sample.mp4' -f ([Net.IPEndPoint]$loadListener.LocalEndpoint).Port
                $arguments.Add(('--recovery-test-load-url={0}' -f $loadUrl))
            }
            if ($scenario -eq 'performance') {
                $arguments.Add(('--recovery-test-warmup-seconds={0}' -f $WarmupSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)))
                $arguments.Add(('--recovery-test-seconds={0}' -f $SteadySeconds.ToString([Globalization.CultureInfo]::InvariantCulture)))
            }
            $scenarioMedia = if ($scenario -eq 'close-during-load') { $loadUrl } elseif ($scenario -eq 'recovery-media-switch') { "$MediaPath → $switchMediaPath" } else { $MediaPath }
            Write-Host "运行应用恢复验证：$scenario；视频：$scenarioMedia；调试层：$D3D11DebugLayer；限时 $limitSeconds 秒"
            # This is the actual interactive test app, not a background helper: it needs
            # a visible SwapChainPanel and an interactive Windows desktop for rendering.
            $process = Start-Process -FilePath $AppPath -ArgumentList $arguments `
                -WorkingDirectory ([IO.Path]::GetDirectoryName($AppPath)) -PassThru
            if (-not $process.WaitForExit($limitSeconds * 1000)) {
                # Keep the process handle so only this test-created process is terminated.
                $process.Kill()
                [void] $process.WaitForExit(5000)
                throw "应用验证超过 $limitSeconds 秒；已终止本次测试启动的应用进程。"
            }
            $exitCode = $process.ExitCode
            if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
                throw "应用未写出恢复报告（进程退出码 $exitCode）；请确认使用支持恢复验证参数的 Debug 版本。"
            }
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -AsHashtable
            $reportStatus = $report['Status']
            $shutdownCompleted = $report['ShutdownCompleted']
            if ($reportStatus -ne 'Passed') {
                $detail = if ($report.ContainsKey('Error')) { $report['Error'] } else { '缺少 Passed 状态。' }
                throw "应用报告恢复验证失败：$detail"
            }
            if ($shutdownCompleted -isnot [bool] -or -not $shutdownCompleted) {
                throw '应用未确认完整释放播放会话和渲染资源：报告须包含 ShutdownCompleted=true。'
            }
            if ($report['SessionReleased'] -isnot [bool] -or -not $report['SessionReleased'] -or
                $report['RendererReleased'] -isnot [bool] -or -not $report['RendererReleased']) {
                throw '应用报告未确认关闭后会话与渲染器均已释放（SessionReleased/RendererReleased）。'
            }
            if ($report['ShutdownRepeatSafe'] -isnot [bool] -or -not $report['ShutdownRepeatSafe']) {
                throw '应用未确认重复关闭幂等（ShutdownRepeatSafe）。'
            }
            # 会话日志必须留下完整释放顺序标记。
            $logFile = Get-ChildItem -LiteralPath $scenarioLogDirectory -Filter 'session-*.log' |
                Sort-Object LastWriteTime | Select-Object -Last 1
            $logText = if ($null -ne $logFile) { Get-Content -LiteralPath $logFile.FullName -Raw } else { '' }
            if (-not $logText.Contains('会话已销毁。') -or
                -not $logText.Contains('渲染上下文、EGL、SwapChain 和 D3D11 资源已按顺序释放。')) {
                throw '会话日志缺少完整释放标记（会话已销毁 / 图形资源已按顺序释放）。'
            }
            if ($exitCode -ne 0) { throw "应用报告通过，但进程退出码为 $exitCode。" }
        }
        catch {
            $failure = $_.Exception.Message
            Write-Warning "$scenario 验证失败：$failure"
        }
        finally {
            $timer.Stop()
            $env:MPVSHELL_LOG_DIRECTORY = $previousLogDirectory
            if ($null -ne $loadListener) { $loadListener.Stop() }
            if ($null -ne $process) { $process.Dispose() }
        }
        $results.Add([ordered]@{
            Mode = $scenario
            Passed = $null -eq $failure
            ExitCode = $exitCode
            ReportStatus = $reportStatus
            ShutdownCompleted = $shutdownCompleted
            Error = $failure
            DurationSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
            ReportPath = $reportPath
            LogDirectory = $scenarioLogDirectory
        })
    }
}
finally {
    $env:MPVSHELL_D3D11_DEBUG_LAYER = $previousDebugLayer
}

$summaryPath = Join-Path $ReportDirectory ('summary-' + $runId + '.json')
$summary = [ordered]@{
    CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    AppPath = $AppPath
    MediaPath = $MediaPath
    SwitchMediaPath = $switchMediaPath
    GeneratedMedia = $generatedMedia
    D3D11DebugLayer = $D3D11DebugLayer
    WarmupSeconds = $WarmupSeconds
    SteadySeconds = $SteadySeconds
    Results = $results.ToArray()
}
$summaryStream = [IO.File]::Open($summaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
$writer = [IO.StreamWriter]::new($summaryStream, [Text.UTF8Encoding]::new($false))
try { $writer.WriteLine(($summary | ConvertTo-Json -Depth 8)) }
finally { $writer.Dispose() }
$results.ToArray() | ForEach-Object { [pscustomobject] $_ } | Format-Table Mode, Passed, ShutdownCompleted, ExitCode, DurationSeconds
Write-Host "汇总报告：$summaryPath"
if (@($results | Where-Object { -not $_.Passed }).Count -gt 0) {
    throw "应用恢复验证存在失败；详情：$summaryPath"
}

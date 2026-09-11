#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $MediaPath,
    [string] $ReportDirectory,
    [ValidateNotNullOrEmpty()]
    [ValidateSet('playing', 'paused', 'immediate-failure', 'rebuild-failure', 'resize', 'playback')]
    [string[]] $Mode = @('playing', 'paused', 'immediate-failure', 'rebuild-failure', 'resize'),
    [string] $AppPath,
    [switch] $NoBuild
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

function Write-RecoverySample([string] $Path) {
    # BT.601 limited-range YUV420: red upper half, blue lower half.
    # Preserve the main colors; a moving white marker also exposes frozen playback.
    $baseFrame = [byte[]]::new(64 * 64 * 3 / 2)
    for ($row = 0; $row -lt 64; $row++) {
        for ($column = 0; $column -lt 64; $column++) {
            $baseFrame[$row * 64 + $column] = if ($row -lt 32) { 81 } else { 41 }
        }
    }
    for ($row = 0; $row -lt 32; $row++) {
        for ($column = 0; $column -lt 32; $column++) {
            $baseFrame[4096 + $row * 32 + $column] = if ($row -lt 16) { 90 } else { 240 }
            $baseFrame[5120 + $row * 32 + $column] = if ($row -lt 16) { 240 } else { 110 }
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

foreach ($scenario in $Mode) {
    $reportPath = Join-Path $ReportDirectory ($scenario + '-' + [Guid]::NewGuid().ToString('N') + '.json')
    $process = $null
    $exitCode = $null
    $failure = $null
    $reportStatus = $null
    $shutdownCompleted = $null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        # Start-Process joins ArgumentList on Windows, so quote entire path-bearing arguments.
        $arguments = @(
            ('"{0}"' -f $MediaPath),
            ('"--recovery-test-report={0}"' -f $reportPath),
            ('--recovery-test-mode={0}' -f $scenario)
        )
        if ($generatedMedia) { $arguments += '--recovery-test-animated-pattern' }
        Write-Host "运行应用恢复验证：$scenario；视频：$MediaPath"
        # This is the actual interactive test app, not a background helper: it needs
        # a visible SwapChainPanel and an interactive Windows desktop for rendering.
        $process = Start-Process -FilePath $AppPath -ArgumentList $arguments `
            -WorkingDirectory ([IO.Path]::GetDirectoryName($AppPath)) -PassThru
        if (-not $process.WaitForExit(90000)) {
            # Keep the process handle so only this test-created process is terminated.
            $process.Kill()
            [void] $process.WaitForExit(5000)
            throw '应用恢复验证超过 90 秒；已终止本次测试启动的应用进程。'
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
        if ($exitCode -ne 0) { throw "应用报告通过，但进程退出码为 $exitCode。" }
    }
    catch {
        $failure = $_.Exception.Message
        Write-Warning "$scenario 验证失败：$failure"
    }
    finally {
        $timer.Stop()
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
    })
}

$summaryPath = Join-Path $ReportDirectory ('summary-' + $runId + '.json')
$summary = [ordered]@{
    CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    AppPath = $AppPath
    MediaPath = $MediaPath
    GeneratedMedia = $generatedMedia
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

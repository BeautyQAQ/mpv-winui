#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $MediaPath,
    [string] $ReportDirectory,
    [ValidateNotNullOrEmpty()]
    [ValidateSet('playing', 'paused', 'immediate-failure', 'rebuild-failure', 'resize', 'playback', 'performance',
        'page-rebuild-playing', 'page-rebuild-paused', 'close-during-recovery', 'close-during-load', 'close-during-startup-load', 'recovery-media-switch', 'continuous')]
    [string[]] $Mode = @('playing', 'paused', 'immediate-failure', 'rebuild-failure', 'resize',
        'page-rebuild-playing', 'page-rebuild-paused', 'close-during-recovery', 'close-during-load', 'close-during-startup-load', 'recovery-media-switch'),
    [string] $AppPath,
    [switch] $NoBuild,
    # 以下仅影响 performance 模式：预热秒数、稳态窗口秒数。
    [ValidateRange(0, 600)] [double] $WarmupSeconds = 3,
    [ValidateRange(1, 600)] [double] $SteadySeconds = 30,
    # continuous 必须显式选入；默认只做两轮短测，不会自动执行长时间测试。
    [string[]] $ContinuousMediaPaths,
    [ValidateRange(1, 10000)] [int] $ContinuousCycles = 2,
    [ValidateRange(0.5, 30)] [double] $ContinuousPlaySeconds = 1.5,
    [ValidateRange(0, 86400)] [double] $ContinuousDurationSeconds = 0,
    # Auto：Debug 应用按默认行为启用 D3D11 调试层（若已安装 Graphics Tools）；Off：通过 MPVSHELL_D3D11_DEBUG_LAYER=0 关闭，
    # 用于在同一 Debug 构建下对照调试层开销。Release 应用不含探针，此开关对其无意义。
    [ValidateSet('Auto', 'Off')] [string] $D3D11DebugLayer = 'Auto',
    # 单个应用进程的限时；performance 模式会按预热与窗口自动放宽。
    [ValidateRange(30, 3600)] [int] $TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not ($Mode -contains 'continuous') -and @($PSBoundParameters.Keys | Where-Object { $_ -like 'Continuous*' }).Count -gt 0) {
    throw 'Continuous 参数只允许用于显式选择的 continuous 模式。'
}
if (-not [double]::IsFinite($ContinuousPlaySeconds) -or -not [double]::IsFinite($ContinuousDurationSeconds)) {
    throw '连续测试的时长必须是有限数值。'
}
function Resolve-ContinuousMedia([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path) -or
        [IO.Path]::GetPathRoot($Path) -notmatch '^[A-Za-z]:[\\/]$' -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "连续测试素材必须是存在的本地绝对文件路径：$Path"
    }
    (Resolve-Path -LiteralPath $Path).Path
}
$continuousMedia = @()
if ($PSBoundParameters.ContainsKey('ContinuousMediaPaths')) {
    $continuousMedia = @($ContinuousMediaPaths | ForEach-Object { Resolve-ContinuousMedia $_ })
    $distinctMediaCount = @($continuousMedia | Sort-Object -Unique).Count
    if ($distinctMediaCount -lt 2 -or $distinctMediaCount -ne $continuousMedia.Count) {
        throw '连续测试至少需要两个不同的本地素材路径，且不能包含重复项。'
    }
    if ($PSBoundParameters.ContainsKey('MediaPath') -and (Resolve-ContinuousMedia $MediaPath) -ne $continuousMedia[0]) {
        throw 'MediaPath 必须与 ContinuousMediaPaths 的第一项一致；拒绝使用冲突的启动素材。'
    }
    $MediaPath = $continuousMedia[0]
}
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

if ($Mode -contains 'continuous' -and $continuousMedia.Count -eq 0) {
    $continuousAlternate = Join-Path $ReportDirectory ('continuous-alternate-' + $runId + '.y4m')
    Write-RecoverySample $continuousAlternate -SwapColors
    $continuousMedia = @($MediaPath, $continuousAlternate)
}

# 恢复中换媒体场景需要第二份不同颜色的样片；与主素材独立生成。
$switchMediaPath = $null
if ($Mode -contains 'recovery-media-switch') {
    [void] [IO.Directory]::CreateDirectory($artifactDirectory)
    $switchMediaPath = Join-Path $artifactDirectory ('red-blue-switch-' + $runId + '.y4m')
    Write-RecoverySample $switchMediaPath -SwapColors
}

if (-not $NoBuild) {
    & dotnet build (Join-Path $repository 'mpv-winui.slnx') -c Debug -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "Debug 应用构建失败，退出码 $LASTEXITCODE。" }
}
if ([string]::IsNullOrWhiteSpace($AppPath)) {
    $AppPath = Join-Path $repository 'src\MpvShell.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\MpvShell.App.exe'
}
if (-not (Test-Path -LiteralPath $AppPath -PathType Leaf)) {
    throw "找不到 Debug 应用：$AppPath。使用 -AppPath 指定已构建的 Debug 版本。"
}
$AppPath = (Resolve-Path -LiteralPath $AppPath).Path
$appDllPath = [IO.Path]::ChangeExtension($AppPath, '.dll')
if (-not (Test-Path -LiteralPath $appDllPath -PathType Leaf)) { throw "找不到应用程序集：$appDllPath" }
$appIdentity = @($AppPath, $appDllPath | ForEach-Object {
    [ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_).Hash }
})
function Write-NewJson([string] $Path, $Value) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    try { $writer.WriteLine(($Value | ConvertTo-Json -Depth 16)) }
    finally { $writer.Dispose() }
}
function Get-PlayerProcesses {
    @(Get-Process -Name @('MpvShell.App', [IO.Path]::GetFileNameWithoutExtension($AppPath)) -ErrorAction SilentlyContinue |
        Sort-Object Id -Unique)
}
function Save-EnvironmentValue([string] $Name) {
    $variable = Get-Item -LiteralPath ('Env:' + $Name) -ErrorAction SilentlyContinue
    @{ Name = $Name; Exists = $null -ne $variable; Value = if ($null -ne $variable) { $variable.Value } else { $null } }
}
function Restore-EnvironmentValue($Saved) {
    # PS7 的空串与不存在是两种状态；用环境提供程序明确恢复原存在性。
    Remove-Item -LiteralPath ('Env:' + $Saved.Name) -ErrorAction SilentlyContinue
    if ($Saved.Exists) { Set-Item -LiteralPath ('Env:' + $Saved.Name) -Value $Saved.Value }
}
function Get-MediaIdentity([string[]] $Paths) {
    @($Paths | Sort-Object -Unique | ForEach-Object {
        [ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_).Hash; Bytes = (Get-Item -LiteralPath $_).Length }
    })
}
function Assert-MediaIdentity($Expected) {
    foreach ($item in $Expected) {
        if (-not (Test-Path -LiteralPath $item.Path -PathType Leaf) -or (Get-FileHash -LiteralPath $item.Path).Hash -ne $item.Sha256) {
            throw "连续测试期间素材发生变化：$($item.Path)"
        }
    }
}
function Stop-OwnedProcess($Owned) {
    if ($Owned.ExitConfirmed) { return }
    if (-not $Owned.Process.HasExited) {
        $Owned.Process.Kill()
        if (-not $Owned.Process.WaitForExit(5000)) { throw "本次测试进程 $($Owned.ProcessId) 尚未退出，保留原历史备份。" }
    }
    $Owned.ExitConfirmed = $true
}

if (@(Get-PlayerProcesses).Count -gt 0) {
    throw '已有 MpvShell 应用正在运行；为保护最近打开记录，本轮不会启动测试或恢复用户历史。'
}
$historyPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MpvShell/recent-history.json'
if (Test-Path -LiteralPath $historyPath) {
    $historyItem = Get-Item -LiteralPath $historyPath -Force
    if ($historyItem.PSIsContainer -or ($historyItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw '最近打开记录必须是普通叶文件，不能是目录或链接。'
    }
}
$historyExisted = Test-Path -LiteralPath $historyPath -PathType Leaf
$historyBytes = [byte[]]::new(0)
if ($historyExisted) { $historyBytes = [IO.File]::ReadAllBytes($historyPath) }
$historyBeforeHash = if ($historyExisted) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$historyBytes)) } else { $null }
$historyBackupPath = if ($historyExisted) { Join-Path $ReportDirectory ('recent-history-before-' + $runId + '.bin') } else { $null }
if ($historyExisted) { [IO.File]::WriteAllBytes($historyBackupPath, [byte[]]$historyBytes) }
$historyRecovery = [ordered]@{ Path = $historyPath; ExistedBefore = $historyExisted; Sha256Before = $historyBeforeHash;
    BackupPath = $historyBackupPath; Restored = $false; Error = $null }
$ownedProcesses = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$previousDebugLayer = Save-EnvironmentValue 'MPVSHELL_D3D11_DEBUG_LAYER'
if ($D3D11DebugLayer -eq 'Off') { $env:MPVSHELL_D3D11_DEBUG_LAYER = '0' }
try {
    foreach ($scenario in $Mode) {
        $reportPath = Join-Path $ReportDirectory ($scenario + '-' + [Guid]::NewGuid().ToString('N') + '.json')
        $process = $null
        $owned = $null
        $processId = $null
        $invocationPath = Join-Path $ReportDirectory ('invocation-' + $scenario + '-' + [Guid]::NewGuid().ToString('N') + '.json')
        $continuousPlanPath = $null
        $continuousPlan = $null
        $continuousCyclesCompleted = $null
        $exitCode = $null
        $failure = $null
        $reportStatus = $null
        $shutdownCompleted = $null
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $limitSeconds = $TimeoutSeconds
        if ($scenario -eq 'performance') {
            $limitSeconds = [Math]::Max($TimeoutSeconds, [int][Math]::Ceiling($WarmupSeconds + $SteadySeconds + 60))
        }
        if ($scenario -eq 'continuous') {
            # 每个媒体包含初播、恢复、重播三次推进及打开、暂停、停止的有限等待；仅放宽此显式模式。
            $cycleBudget = $continuousMedia.Count * (20 + 3 * $ContinuousPlaySeconds)
            $continuousBudget = if ($ContinuousDurationSeconds -gt 0) { $ContinuousDurationSeconds + $cycleBudget + 60 }
                else { $ContinuousCycles * $cycleBudget + 60 }
            if (-not $PSBoundParameters.ContainsKey('TimeoutSeconds')) {
                $limitSeconds = [Math]::Max($TimeoutSeconds, [Math]::Ceiling($continuousBudget))
            }
        }
        $loadListener = $null
        $scenarioLogDirectory = Join-Path $ReportDirectory ('logs-' + $scenario + '-' + [Guid]::NewGuid().ToString('N'))
        [void] [IO.Directory]::CreateDirectory($scenarioLogDirectory)
        $previousLogDirectory = Save-EnvironmentValue 'MPVSHELL_LOG_DIRECTORY'
        $env:MPVSHELL_LOG_DIRECTORY = $scenarioLogDirectory
        try {
            if (@(Get-PlayerProcesses).Count -gt 0) { throw '启动前发现其他 MpvShell 应用；拒绝并发修改最近打开记录。' }
            # Start-Process joins ArgumentList on Windows, so quote entire path-bearing arguments.
            $arguments = [Collections.Generic.List[string]]::new()
            $loadingClose = $scenario -in @('close-during-load', 'close-during-startup-load')
            if (-not $loadingClose) { $arguments.Add(('"{0}"' -f $MediaPath)) }
            $arguments.Add(('"--recovery-test-report={0}"' -f $reportPath))
            $arguments.Add(('--recovery-test-mode={0}' -f $scenario))
            if ($generatedMedia -and -not $loadingClose) { $arguments.Add('--recovery-test-animated-pattern') }
            if ($scenario -eq 'recovery-media-switch') { $arguments.Add(('"--recovery-test-switch-media={0}"' -f $switchMediaPath)) }
            if ($loadingClose) {
                # 只接受连接、不响应请求的本机监听端口：加载真实停留在进行中，直到应用关闭。
                $loadListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
                $loadListener.Start()
                $loadUrl = 'http://127.0.0.1:{0}/sample.mp4' -f ([Net.IPEndPoint]$loadListener.LocalEndpoint).Port
                if ($scenario -eq 'close-during-startup-load') {
                    # 真正走 PlayerPage.OnLoaded 的位置启动参数，探针不得另发一次加载代替它。
                    $arguments.Add(('"{0}"' -f $loadUrl))
                }
                else { $arguments.Add(('--recovery-test-load-url={0}' -f $loadUrl)) }
            }
            if ($scenario -eq 'performance') {
                $arguments.Add(('--recovery-test-warmup-seconds={0}' -f $WarmupSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)))
                $arguments.Add(('--recovery-test-seconds={0}' -f $SteadySeconds.ToString([Globalization.CultureInfo]::InvariantCulture)))
            }
            $scenarioMediaInputs = Get-MediaIdentity @($MediaPath)
            if ($scenario -eq 'recovery-media-switch') { $scenarioMediaInputs = Get-MediaIdentity @($MediaPath, $switchMediaPath) }
            if ($scenario -eq 'continuous') {
                $continuousPlan = [ordered]@{ MediaPaths = $continuousMedia; Cycles = $ContinuousCycles;
                    PlaySeconds = $ContinuousPlaySeconds; DurationSeconds = $ContinuousDurationSeconds }
                $continuousPlanPath = Join-Path $ReportDirectory ('continuous-plan-' + [Guid]::NewGuid().ToString('N') + '.json')
                Write-NewJson $continuousPlanPath $continuousPlan
                $scenarioMediaInputs = Get-MediaIdentity $continuousMedia
                $arguments.Add(('"--recovery-test-continuous-plan={0}"' -f $continuousPlanPath))
            }
            $scenarioMedia = if ($loadingClose) { $loadUrl } elseif ($scenario -eq 'recovery-media-switch') { "$MediaPath → $switchMediaPath" } else { $MediaPath }
            Write-Host "运行应用恢复验证：$scenario；视频：$scenarioMedia；调试层：$D3D11DebugLayer；限时 $limitSeconds 秒"
            # This is the actual interactive test app, not a background helper: it needs
            # a visible SwapChainPanel and an interactive Windows desktop for rendering.
            $process = Start-Process -FilePath $AppPath -ArgumentList $arguments `
                -WorkingDirectory ([IO.Path]::GetDirectoryName($AppPath)) -PassThru
            $processId = $process.Id
            $owned = [pscustomobject]@{ Process = $process; ProcessId = $processId; ExitConfirmed = $false }
            $ownedProcesses.Add($owned)
            Write-NewJson $invocationPath ([ordered]@{ StartedAtUtc = [DateTimeOffset]::UtcNow.ToString('O');
                Mode = $scenario; ProcessId = $processId; AppIdentity = $appIdentity; MediaInputs = $scenarioMediaInputs;
                Arguments = $arguments.ToArray(); LimitSeconds = $limitSeconds; D3D11DebugLayer = $D3D11DebugLayer;
                LogDirectory = $scenarioLogDirectory; ReportPath = $reportPath; ContinuousPlanPath = $continuousPlanPath;
                ContinuousPlan = $continuousPlan; ContinuousPlanSha256 = if ($continuousPlanPath) { (Get-FileHash -LiteralPath $continuousPlanPath).Hash } else { $null } })
            # 分段等待避免 continuous 的可配置预算超出 WaitForExit 的 32-bit 毫秒范围。
            while (-not $process.WaitForExit(1000)) {
                if ($timer.Elapsed.TotalSeconds -gt $limitSeconds) {
                    Stop-OwnedProcess $owned
                    throw "应用验证超过 $limitSeconds 秒；已终止本次测试启动的应用进程。"
                }
            }
            $owned.ExitConfirmed = $true
            $exitCode = $process.ExitCode
            Assert-MediaIdentity $scenarioMediaInputs
            if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
                throw "应用未写出恢复报告（进程退出码 $exitCode）；请确认使用支持恢复验证参数的 Debug 版本。"
            }
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -AsHashtable
            $reportStatus = $report['Status']
            $shutdownCompleted = $report['ShutdownCompleted']
            if ($reportStatus -ne 'Passed') {
                $detail = if ($report.ContainsKey('Error')) { $report['Error'] }
                    elseif ($report.ContainsKey('ShutdownError')) { $report['ShutdownError'] }
                    else { '缺少 Passed 状态。' }
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
            if ($scenario -eq 'continuous') {
                if ($report['ProcessId'] -ne $processId -or $report['ContinuousCompleted'] -isnot [bool] -or -not $report['ContinuousCompleted']) {
                    throw '连续测试未确认同一进程完整完成（ProcessId/ContinuousCompleted）。'
                }
                $actualPlan = $report['ContinuousPlan']
                if ($actualPlan -isnot [Collections.IDictionary] -or $actualPlan['Cycles'] -ne $ContinuousCycles -or
                    $actualPlan['PlaySeconds'] -ne $ContinuousPlaySeconds -or $actualPlan['DurationSeconds'] -ne $ContinuousDurationSeconds -or
                    (@($actualPlan['MediaPaths']) -join "`n") -cne ($continuousMedia -join "`n")) {
                    throw '连续测试报告的计划与本轮输入不一致。'
                }
                $continuousCyclesCompleted = $report['ContinuousCyclesCompleted']
                if ($continuousCyclesCompleted -isnot [long] -and $continuousCyclesCompleted -isnot [int]) { throw '连续测试报告缺少整数完成轮次。' }
                if ($continuousCyclesCompleted -lt 1 -or ($ContinuousDurationSeconds -eq 0 -and $continuousCyclesCompleted -ne $ContinuousCycles)) {
                    throw '连续测试完成轮次与请求不符。'
                }
                $steps = @($report['ContinuousSteps'])
                $sessionInstanceId = $report['SessionInstanceId']
                if ([string]::IsNullOrWhiteSpace([string]$sessionInstanceId)) { throw '连续测试缺少共享播放会话身份。' }
                foreach ($step in $steps) {
                    if ($step -isnot [Collections.IDictionary] -or $step['ProcessId'] -ne $processId -or
                        $step['SessionInstanceId'] -ne $sessionInstanceId -or
                        $step['Completed'] -isnot [bool] -or -not $step['Completed'] -or
                        ($step['Cycle'] -isnot [long] -and $step['Cycle'] -isnot [int]) -or
                        ($step['MediaIndex'] -isnot [long] -and $step['MediaIndex'] -isnot [int]) -or
                        $step['Cycle'] -lt 1 -or $step['Cycle'] -gt $continuousCyclesCompleted -or
                        $step['MediaIndex'] -lt 1 -or $step['MediaIndex'] -gt $continuousMedia.Count -or
                        $step['MediaPath'] -ne $continuousMedia[$step['MediaIndex'] - 1]) {
                        throw '连续测试步骤存在未完成、不同进程或不属于计划的媒体/轮次。'
                    }
                }
                for ($cycle = 1; $cycle -le $continuousCyclesCompleted; $cycle++) {
                    for ($mediaIndex = 1; $mediaIndex -le $continuousMedia.Count; $mediaIndex++) {
                        if (@($steps | Where-Object { $_['Cycle'] -eq $cycle -and $_['MediaIndex'] -eq $mediaIndex }).Count -lt 1) {
                            throw "连续测试缺少第 $cycle 轮第 $mediaIndex 项媒体的完成记录。"
                        }
                    }
                }
            }
            if ($scenario -eq 'close-during-startup-load') {
                if ($report['StartupMediaArgument'] -ne $loadUrl -or
                    $report['StartupLoadPendingBeforeShutdown'] -isnot [bool] -or -not $report['StartupLoadPendingBeforeShutdown'] -or
                    $report['PendingLoadEndedAfterShutdown'] -isnot [bool] -or -not $report['PendingLoadEndedAfterShutdown'] -or
                    -not $report.ContainsKey('StartupLoadCompletionSeconds') -or
                    $report['ShutdownSeconds'] -gt 5 -or $report['StartupLoadCompletionSeconds'] -gt 5) {
                    throw '启动加载关闭未确认真实启动参数、关闭前仍在等待、5 秒内释放及加载等待结束。'
                }
            }
            # 会话日志必须留下完整释放顺序标记。
            $logFile = Get-ChildItem -LiteralPath $scenarioLogDirectory -Filter 'session-*.log' |
                Sort-Object LastWriteTime | Select-Object -Last 1
            $logText = if ($null -ne $logFile) { Get-Content -LiteralPath $logFile.FullName -Raw } else { '' }
            if (-not $logText.Contains('会话已销毁。') -or
                -not $logText.Contains('渲染上下文、EGL、SwapChain 和 D3D11 资源已按顺序释放。')) {
                throw '会话日志缺少完整释放标记（会话已销毁 / 图形资源已按顺序释放）。'
            }
            if ($scenario -eq 'continuous') {
                $rendererReleasedAt = $logText.LastIndexOf('渲染上下文、EGL、SwapChain 和 D3D11 资源已按顺序释放。', [StringComparison]::Ordinal)
                $sessionReleasedAt = $logText.LastIndexOf('会话已销毁。', [StringComparison]::Ordinal)
                $windowClosedAt = $logText.LastIndexOf('[app] 主窗口已关闭，播放资源已释放。', [StringComparison]::Ordinal)
                if ($rendererReleasedAt -lt 0 -or $sessionReleasedAt -le $rendererReleasedAt -or $windowClosedAt -le $sessionReleasedAt) {
                    throw '连续测试日志未确认渲染资源释放、会话销毁、主窗口关闭的顺序。'
                }
            }
            if ($exitCode -ne 0) { throw "应用报告通过，但进程退出码为 $exitCode。" }
        }
        catch {
            $failure = $_.Exception.Message
            Write-Warning "$scenario 验证失败：$failure"
        }
        finally {
            $timer.Stop()
            Restore-EnvironmentValue $previousLogDirectory
            if ($null -ne $loadListener) { $loadListener.Stop() }
            if ($null -ne $owned) {
                try {
                    $wasStillRunning = -not $owned.Process.HasExited
                    Stop-OwnedProcess $owned
                    if ($wasStillRunning -and $null -eq $failure) { $failure = '验证异常结束，已清理本次启动的测试进程；不计正常退出。' }
                }
                catch {
                    $cleanupFailure = $_.Exception.Message
                    $failure = if ($null -eq $failure) { $cleanupFailure } else { "$failure；收尾失败：$cleanupFailure" }
                    Write-Warning $failure
                }
            }
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
            ProcessId = $processId
            InvocationPath = $invocationPath
            ContinuousPlanPath = $continuousPlanPath
            ContinuousCyclesCompleted = $continuousCyclesCompleted
        })
        if ($null -ne $owned -and -not $owned.ExitConfirmed) { break }
    }
}
finally {
    Restore-EnvironmentValue $previousDebugLayer
    # 各异常路径都只处理本脚本保存的进程句柄；确认全部退出后才能恢复用户历史。
    foreach ($owned in $ownedProcesses) {
        try { Stop-OwnedProcess $owned }
        catch { $historyRecovery.Error = $_.Exception.Message }
        finally { if ($owned.ExitConfirmed) { $owned.Process.Dispose() } }
    }
    try {
        if (@($ownedProcesses | Where-Object { -not $_.ExitConfirmed }).Count -gt 0) { throw '本轮仍有测试进程未退出，不能恢复用户历史。' }
        if (@(Get-PlayerProcesses).Count -gt 0) { throw '测试期间出现其他 MpvShell 应用，保留备份并停止历史恢复，避免覆盖该应用的新记录。' }
        if (Test-Path -LiteralPath $historyPath) {
            $currentHistory = Get-Item -LiteralPath $historyPath -Force
            if ($currentHistory.PSIsContainer -or ($currentHistory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw '历史路径不再是普通叶文件，保留备份并停止恢复。'
            }
        }
        if ($historyExisted) {
            [IO.File]::WriteAllBytes($historyPath, $historyBytes)
        }
        elseif (Test-Path -LiteralPath $historyPath -PathType Leaf) {
            Remove-Item -LiteralPath $historyPath
        }
        $historyRecovery['ExistedAfter'] = Test-Path -LiteralPath $historyPath -PathType Leaf
        $historyRecovery['Sha256After'] = if ($historyRecovery.ExistedAfter) { (Get-FileHash -LiteralPath $historyPath).Hash } else { $null }
        $historyRecovery.Restored = $historyRecovery.ExistedAfter -eq $historyExisted -and $historyRecovery.Sha256After -eq $historyBeforeHash
        if (-not $historyRecovery.Restored) { throw '用户历史恢复后的存在性或字节哈希与原始快照不符。' }
    }
    catch {
        $historyRecovery.Error = $_.Exception.Message
        Write-Warning "最近打开记录未恢复：$($historyRecovery.Error)；原始备份：$historyBackupPath"
    }
    Write-NewJson (Join-Path $ReportDirectory ('history-recovery-' + $runId + '.json')) $historyRecovery
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
    AppIdentity = $appIdentity
    ContinuousMediaPaths = $continuousMedia
    ContinuousCycles = $ContinuousCycles
    ContinuousPlaySeconds = $ContinuousPlaySeconds
    ContinuousDurationSeconds = $ContinuousDurationSeconds
    HistoryRecovery = $historyRecovery
    Results = $results.ToArray()
}
$summaryStream = [IO.File]::Open($summaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
$writer = [IO.StreamWriter]::new($summaryStream, [Text.UTF8Encoding]::new($false))
try { $writer.WriteLine(($summary | ConvertTo-Json -Depth 8)) }
finally { $writer.Dispose() }
$results.ToArray() | ForEach-Object { [pscustomobject] $_ } | Format-Table Mode, Passed, ShutdownCompleted, ExitCode, DurationSeconds
Write-Host "汇总报告：$summaryPath"
if (-not $historyRecovery.Restored -or @($results | Where-Object { -not $_.Passed }).Count -gt 0) {
    throw "应用恢复验证存在失败；详情：$summaryPath"
}

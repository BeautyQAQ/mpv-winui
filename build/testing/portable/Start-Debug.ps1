[CmdletBinding()]
param([string]$MediaPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Portable.Common.ps1')
$runDirectory = $null
$runInfo = $null
$process = $null
$standardOutputFile = $null
$standardErrorFile = $null
$scriptExitCode = 1

try {
    $packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
    $packageKey = Get-TestPackageKey -PackageRoot $packageRoot
    $applicationPath = Join-Path $packageRoot 'MpvShell.App.exe'
    $logsRoot = Join-Path $packageRoot 'logs'
    if (-not (Test-TestDirectoryWritable -Path $logsRoot)) {
        $logsRoot = Join-Path (Get-TestFallbackRoot -PackageKey $packageKey) 'logs'
        if (-not (Test-TestDirectoryWritable -Path $logsRoot)) { throw "日志目录无法写入：$logsRoot" }
    }
    $runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
    $runDirectory = Join-Path $logsRoot $runId
    [void][IO.Directory]::CreateDirectory($runDirectory)
    $runInfoPath = Join-Path $runDirectory 'run-info.json'
    $runInfo = [ordered]@{
        packageKey = $packageKey
        packageRoot = $packageRoot
        runId = $runId
        startedAtUtc = [DateTime]::UtcNow.ToString('o')
        endedAtUtc = $null
        applicationPath = $applicationPath
        mediaPath = $MediaPath
        logDirectory = $runDirectory
        logLevel = 'debug'
        state = 'Starting'
        applicationProcessId = $null
        applicationStartTimeUtc = $null
        exitCode = $null
        launcherError = $null
    }
    Write-TestJson -Path $runInfoPath -Value $runInfo
    $manifestPath = Join-Path $packageRoot 'build-info.json'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $runDirectory 'build-info.json')
    }
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw "未找到播放器：$applicationPath。请完整解压测试包后再启动。"
    }
    if ($MediaPath) {
        if (-not (Test-Path -LiteralPath $MediaPath -PathType Leaf)) { throw "未找到媒体文件：$MediaPath" }
        $MediaPath = (Resolve-Path -LiteralPath $MediaPath).ProviderPath
        $runInfo.mediaPath = $MediaPath
    }

    Write-Host "正在收集 Windows 和显卡驱动信息。日志目录：$runDirectory"
    $environmentInfo = [ordered]@{
        collectedAtUtc = [DateTime]::UtcNow.ToString('o')
        osVersion = [Environment]::OSVersion.VersionString
        is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        windows = $null
        graphics = @()
        processors = @()
        collectionErrors = @()
    }
    try {
        $environmentInfo.windows = Get-CimInstance -ClassName Win32_OperatingSystem -OperationTimeoutSec 10 |
            Select-Object Caption, Version, BuildNumber, OSArchitecture, TotalVisibleMemorySize
    }
    catch { $environmentInfo.collectionErrors += "Windows: $($_.Exception.Message)" }
    try {
        $environmentInfo.graphics = @(Get-CimInstance -ClassName Win32_VideoController -OperationTimeoutSec 10 |
            Select-Object Name, DriverVersion, DriverDate, VideoProcessor, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate)
    }
    catch { $environmentInfo.collectionErrors += "GPU: $($_.Exception.Message)" }
    try {
        $environmentInfo.processors = @(Get-CimInstance -ClassName Win32_Processor -OperationTimeoutSec 10 |
            Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
    }
    catch { $environmentInfo.collectionErrors += "CPU: $($_.Exception.Message)" }
    Write-TestJson -Path (Join-Path $runDirectory 'environment.json') -Value $environmentInfo

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $applicationPath
    $startInfo.WorkingDirectory = $packageRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables['MPVSHELL_LOG_LEVEL'] = 'debug'
    $startInfo.EnvironmentVariables['MPVSHELL_LOG_DIRECTORY'] = $runDirectory
    if ($MediaPath) { $startInfo.Arguments = '"' + $MediaPath + '"' }
    # 播放器是需要用户操作的 GUI，保留可见窗口；不启动辅助可见进程。
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    $standardOutputFile = [IO.File]::Create((Join-Path $runDirectory 'process-stdout.log'))
    $standardErrorFile = [IO.File]::Create((Join-Path $runDirectory 'process-stderr.log'))
    if (-not $process.Start()) { throw '播放器进程启动失败。' }
    # 异步复制标准流，保留日志初始化之前的错误，并防止管道填满阻塞播放器。
    $standardOutputCopy = $process.StandardOutput.BaseStream.CopyToAsync($standardOutputFile)
    $standardErrorCopy = $process.StandardError.BaseStream.CopyToAsync($standardErrorFile)
    $runInfo.applicationProcessId = $process.Id
    try { $runInfo.applicationStartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o') }
    catch { $runInfo.applicationStartTimeUtc = $runInfo.startedAtUtc }
    $runInfo.state = 'Running'
    Write-TestJson -Path $runInfoPath -Value $runInfo
    Write-Host '播放器已启动。请保留此窗口；完成测试后关闭播放器，再运行 Collect-Logs.cmd。'
    $process.WaitForExit()
    $scriptExitCode = $process.ExitCode
    $runInfo.exitCode = $scriptExitCode
    $runInfo.state = 'Exited'
    $standardOutputCopy.GetAwaiter().GetResult()
    $standardErrorCopy.GetAwaiter().GetResult()
    Write-Host "播放器已退出，退出码：$scriptExitCode。日志：$runDirectory"
}
catch {
    $scriptExitCode = 1
    if ($runInfo) {
        $runInfo.state = 'LauncherFailed'
        $runInfo.launcherError = $_.Exception.ToString()
    }
    Write-Host "启动或记录日志失败：$($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if ($runInfo -and $runDirectory) {
        $runInfo.endedAtUtc = [DateTime]::UtcNow.ToString('o')
        try { Write-TestJson -Path (Join-Path $runDirectory 'run-info.json') -Value $runInfo }
        catch { Write-Warning "无法写入最终运行记录：$($_.Exception.Message)" }
    }
    if ($process) { $process.Dispose() }
    if ($standardOutputFile) { $standardOutputFile.Dispose() }
    if ($standardErrorFile) { $standardErrorFile.Dispose() }
}
exit $scriptExitCode

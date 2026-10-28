#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ReportDirectory,
    [ValidateSet('Default', 'Explicit', 'All')][string] $TestMode = 'All',
    [string] $MediaInputsPath,
    [string] $AssessmentDate = (Get-Date -Format 'yyyy-MM-dd')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$reportRoot = [IO.Path]::GetFullPath($ReportDirectory)
$artifactPrefix = (Join-Path $repository 'artifacts') + [IO.Path]::DirectorySeparatorChar
if (-not $reportRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '报告目录必须位于仓库 artifacts 内。' }
if (Test-Path -LiteralPath $reportRoot) { throw '报告目录已存在，请使用新的目录保留首次结果。' }
$media = if ($TestMode -ne 'Default') {
    if (-not $MediaInputsPath) { throw '显式测试必须提供素材输入清单。' }
    Get-Content -LiteralPath $MediaInputsPath -Raw | ConvertFrom-Json -AsHashtable
} else { $null }
if ($media -and -not $media.environment) { throw '素材清单缺少 environment 对象。' }
[void][IO.Directory]::CreateDirectory($reportRoot)
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $reportRoot 'test-candidate-baseline.executed.ps1')
if ($media) { Copy-Item -LiteralPath $MediaInputsPath -Destination (Join-Path $reportRoot 'media-inputs.json') }

function Save-Json([string] $Name, $Value) {
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $reportRoot $Name) -Encoding utf8NoBOM
}
function Get-BuildInputs {
    $paths = @(& git -C $repository -c core.quotepath=false ls-files --cached --others --exclude-standard -- src tests '*.props' '*.targets' mpv-winui.slnx)
    if ($LASTEXITCODE -ne 0) { throw '无法枚举构建输入。' }
    @($paths | Sort-Object -Unique | ForEach-Object {
        $path = Join-Path $repository $_
        [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $path).Hash }
    })
}
function Invoke-Dotnet([string] $Name, [string[]] $Arguments) {
    $log = Join-Path $reportRoot ($Name + '.log')
    $started = [DateTimeOffset]::UtcNow
    & dotnet @Arguments *> $log
    $code = $LASTEXITCODE
    $script:commands.Add([ordered]@{ name = $Name; arguments = $Arguments; startedAtUtc = $started.ToString('O'); endedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); exitCode = $code; log = $log })
    Save-Json 'commands.json' $script:commands.ToArray()
    Write-Host "$Name 完成，退出码 $code；日志：$log"
    return $code
}
function Clear-TestEnvironment {
    foreach ($variable in @(Get-ChildItem Env: | Where-Object Name -like 'MPVSHELL_TEST_*')) {
        [Environment]::SetEnvironmentVariable($variable.Name, $null, 'Process')
    }
}
function Test-MediaInputs([string] $Name) {
    if (-not $media) { return }
    if (-not $media.inputs -or -not $media.manifests) { throw '素材清单缺少 inputs 或 manifests 哈希。' }
    $checks = @(@($media.inputs) + @($media.manifests) | ForEach-Object {
        $actual = if (Test-Path -LiteralPath $_.path -PathType Leaf) { (Get-FileHash -LiteralPath $_.path).Hash } else { $null }
        [ordered]@{ path = $_.path; expected = $_.sha256; actual = $actual; matched = $null -ne $actual -and $actual -eq $_.sha256 }
    })
    Save-Json $Name $checks
    if (@($checks | Where-Object { -not $_.matched }).Count) { throw '素材或清单哈希发生变化，不能沿用已登记输入。' }
}
function Save-BinaryIdentity([string] $Configuration) {
    $files = @(foreach ($project in 'MpvShell.App', 'MpvShell.Player.Abstractions', 'MpvShell.Player.LibMpv', 'MpvShell.Rendering.WinUI') {
        Get-ChildItem -LiteralPath (Join-Path $repository "src/$project/bin/x64/$Configuration") -Recurse -File |
            Where-Object Name -in @('MpvShell.App.exe', 'MpvShell.App.dll', 'MpvShell.Player.Abstractions.dll', 'MpvShell.Player.LibMpv.dll', 'MpvShell.Rendering.WinUI.dll')
    })
    Save-Json "$Configuration-binaries.json" @($files | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($repository, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
}

$commands = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$originalEnvironment = @{}
foreach ($variable in Get-ChildItem Env: | Where-Object Name -like 'MPVSHELL_TEST_*') { $originalEnvironment[$variable.Name] = $variable.Value }
$originalHttp = $env:HTTP_PROXY
$originalHttps = $env:HTTPS_PROXY
$initialInputs = Get-BuildInputs
Save-Json 'build-inputs.json' $initialInputs
Save-Json 'environment.json' ([ordered]@{
    assessmentDate = $AssessmentDate; capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    commit = (& git -C $repository rev-parse HEAD); sourceStatus = @(& git -C $repository status --porcelain)
    sdk = (& dotnet --version); powershell = $PSVersionTable.PSVersion.ToString(); os = [Environment]::OSVersion.VersionString
    runnerSha256 = (Get-FileHash -LiteralPath (Join-Path $reportRoot 'test-candidate-baseline.executed.ps1')).Hash
    graphics = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution)
    testMode = $TestMode; note = '功能及测量测试结果不替代性能判定或其他硬件验收。'
})
$failed = $false
Push-Location $repository
try {
    Test-MediaInputs 'media-validation-before.json'
    $env:HTTP_PROXY = 'http://127.0.0.1:7890'
    $env:HTTPS_PROXY = 'http://127.0.0.1:7890'
    foreach ($configuration in 'Debug', 'Release') {
        Clear-TestEnvironment
        $buildCode = Invoke-Dotnet "$configuration-build" @('build', 'mpv-winui.slnx', '-p:Platform=x64', '-c', $configuration, '-m:1', '-v:minimal')
        if ($buildCode -ne 0) { $failed = $true; continue }
        Save-BinaryIdentity $configuration
        $modes = if ($TestMode -eq 'All') { @('Default', 'Explicit') } else { @($TestMode) }
        foreach ($mode in $modes) {
            Clear-TestEnvironment
            if ($mode -eq 'Explicit') {
                foreach ($key in $media.environment.Keys) {
                    if ($key -notlike 'MPVSHELL_TEST_*') { throw "不允许设置非测试环境变量：$key" }
                    [Environment]::SetEnvironmentVariable($key, [string]$media.environment[$key], 'Process')
                }
                $env:MPVSHELL_TEST_HARDWARE_REPORT_DIR = Join-Path $reportRoot "$configuration-explicit-hardware"
                $env:MPVSHELL_TEST_P1_04_REPORT_DIR = Join-Path $reportRoot "$configuration-explicit-p1-04"
            }
            $name = "$configuration-$($mode.ToLowerInvariant())"
            $trxDirectory = Join-Path $reportRoot $name
            $code = Invoke-Dotnet $name @('test', 'mpv-winui.slnx', '-p:Platform=x64', '-c', $configuration, '--no-build', '--no-restore', '-m:1', '--logger', 'trx', '--results-directory', $trxDirectory)
            $outcomes = @()
            $reports = @(Get-ChildItem -LiteralPath $trxDirectory -Filter '*.trx' -ErrorAction SilentlyContinue)
            foreach ($file in $reports) {
                [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
                foreach ($item in $trx.TestRun.Results.UnitTestResult) {
                    $outcomes += [ordered]@{ testName = $item.testName; outcome = $item.outcome; report = $file.Name }
                }
            }
            $summary = [ordered]@{
                name = $name; exitCode = $code; total = $outcomes.Count; reportCount = $reports.Count
                passed = @($outcomes | Where-Object outcome -eq 'Passed').Count
                failed = @($outcomes | Where-Object outcome -eq 'Failed').Count
                skipped = @($outcomes | Where-Object outcome -eq 'NotExecuted').Count
                other = @($outcomes | Where-Object outcome -notin @('Passed', 'Failed', 'NotExecuted')).Count
                tests = $outcomes
            }
            $results.Add($summary)
            Save-Json 'regression-summary.json' $results.ToArray()
            if ($code -ne 0 -or $reports.Count -ne 4 -or $outcomes.Count -eq 0 -or $summary.failed -gt 0 -or $summary.other -gt 0 -or ($mode -eq 'Explicit' -and $summary.skipped -gt 0)) { $failed = $true }
            Write-Host "$name：$($summary.passed) 通过，$($summary.failed) 失败，$($summary.skipped) 跳过。"
        }
    }
    Test-MediaInputs 'media-validation-after.json'
}
catch {
    $failed = $true
    Save-Json 'failure.json' ([ordered]@{ message = $_.Exception.ToString(); scriptStackTrace = $_.ScriptStackTrace })
    throw
}
finally {
    Clear-TestEnvironment
    foreach ($key in $originalEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $originalEnvironment[$key], 'Process') }
    $env:HTTP_PROXY = $originalHttp
    $env:HTTPS_PROXY = $originalHttps
    Pop-Location
    $finalInputs = Get-BuildInputs
    $same = -not (Compare-Object @($initialInputs | ForEach-Object { $_.path + ':' + $_.sha256 }) @($finalInputs | ForEach-Object { $_.path + ':' + $_.sha256 }))
    $expectedCommands = if ($TestMode -eq 'All') { 6 } else { 4 }
    if ($commands.Count -ne $expectedCommands) { $failed = $true }
    Save-Json 'closure-verification.json' ([ordered]@{ completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); buildInputsUnchanged = $same; inputCount = $initialInputs.Count; commands = $commands.ToArray(); allCommandsPassed = -not $failed -and $same; completedCommands = $commands.Count; expectedCommands = $expectedCommands })
    if (-not $same) { throw '验证期间构建输入发生变化，本轮不能用作冻结基线。' }
}
if ($failed) { exit 1 }

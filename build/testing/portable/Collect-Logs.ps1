[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Portable.Common.ps1')
$archive = $null
$archiveStream = $null
$archivePath = $null
$archiveCreated = $false

try {
    $packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
    $packageKey = Get-TestPackageKey -PackageRoot $packageRoot
    $fallbackRoot = Get-TestFallbackRoot -PackageKey $packageKey
    $logRoots = @((Join-Path $packageRoot 'logs'), (Join-Path $fallbackRoot 'logs')) | Select-Object -Unique
    $runs = @()
    foreach ($logRoot in $logRoots) {
        if (-not (Test-Path -LiteralPath $logRoot -PathType Container)) { continue }
        foreach ($directory in Get-ChildItem -LiteralPath $logRoot -Directory) {
            $runInfoPath = Join-Path $directory.FullName 'run-info.json'
            if (-not (Test-Path -LiteralPath $runInfoPath -PathType Leaf)) { continue }
            try { $run = Get-Content -LiteralPath $runInfoPath -Encoding UTF8 -Raw | ConvertFrom-Json }
            catch { Write-Warning "跳过无法解析的运行记录：$runInfoPath"; continue }
            if ($run.packageKey -ne $packageKey) { continue }
            if ($run.applicationProcessId) {
                $runningProcess = Get-Process -Id $run.applicationProcessId -ErrorAction SilentlyContinue
                if ($runningProcess) {
                    $sameProcess = $false
                    try {
                        if ($run.applicationStartTimeUtc) {
                            $recordedStart = [DateTime]::Parse($run.applicationStartTimeUtc).ToUniversalTime()
                            $sameProcess = [Math]::Abs(($runningProcess.StartTime.ToUniversalTime() - $recordedStart).TotalSeconds) -lt 2
                        }
                        else { $sameProcess = $runningProcess.Path -eq $run.applicationPath }
                    }
                    finally { $runningProcess.Dispose() }
                    if ($sameProcess) { throw "播放器仍在运行，请先关闭播放器及等待启动窗口显示退出码：$($directory.Name)" }
                }
            }
            $runs += [pscustomobject]@{ Directory = $directory.FullName; Name = $directory.Name; Info = $run }
        }
    }
    if ($runs.Count -eq 0) { throw '未找到本测试包的运行日志。请先双击 Start-Debug.cmd 完成一次测试，再收集日志。' }

    $outputDirectory = $null
    $candidateDirectories = @((Split-Path -Path $packageRoot -Parent), $packageRoot, $fallbackRoot) | Select-Object -Unique
    foreach ($candidate in $candidateDirectories) {
        if ($candidate -and (Test-TestDirectoryWritable -Path $candidate)) { $outputDirectory = $candidate; break }
    }
    if (-not $outputDirectory) { throw '找不到可写入日志压缩包的目录。' }
    $archiveName = 'MpvShell-RTX3070-Logs-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.zip'
    $archivePath = Join-Path $outputDirectory $archiveName
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
    $archiveCreated = $true
    $archive = New-Object IO.Compression.ZipArchive($archiveStream, [IO.Compression.ZipArchiveMode]::Create)

    foreach ($run in $runs) {
        foreach ($file in Get-ChildItem -LiteralPath $run.Directory -File -Recurse) {
            $relativePath = $file.FullName.Substring($run.Directory.Length).TrimStart('\')
            $entryName = ('logs/' + $run.Name + '/' + $relativePath.Replace('\', '/'))
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    foreach ($name in @('build-info.json', 'source-hashes.json', 'publish-verification.json', 'README-测试说明.md', 'Test-Notes.txt')) {
        $sourcePath = Join-Path $packageRoot $name
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $sourcePath, $name, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    $index = [ordered]@{
        collectedAtUtc = [DateTime]::UtcNow.ToString('o')
        packageKey = $packageKey
        packageRoot = $packageRoot
        runCount = $runs.Count
        runs = @($runs | ForEach-Object { [ordered]@{ runId = $_.Name; sourceDirectory = $_.Directory; state = $_.Info.state; exitCode = $_.Info.exitCode } })
    }
    $indexEntry = $archive.CreateEntry('collection-info.json')
    $writer = New-Object IO.StreamWriter($indexEntry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write(($index | ConvertTo-Json -Depth 6)) }
    finally { $writer.Dispose() }
    $archive.Dispose()
    $archive = $null
    $archiveStream.Dispose()
    $archiveStream = $null
    Write-Host "已收集 $($runs.Count) 次测试记录。请将此压缩包带回开发机：" -ForegroundColor Green
    Write-Output $archivePath
}
catch {
    if ($archive) { $archive.Dispose(); $archive = $null }
    if ($archiveStream) { $archiveStream.Dispose(); $archiveStream = $null }
    if ($archiveCreated -and $archivePath -and [IO.File]::Exists($archivePath)) { [IO.File]::Delete($archivePath) }
    Write-Host "日志收集失败：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($archive) { $archive.Dispose() }
    if ($archiveStream) { $archiveStream.Dispose() }
}

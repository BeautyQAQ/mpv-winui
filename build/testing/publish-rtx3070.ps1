#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $NoRestore,
    [switch] $IncludeTestMedia
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$packageId = [Guid]::NewGuid().ToString('N')
$packageName = 'MpvShell-rtx3070-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $packageId.Substring(0, 6)
$outputRoot = Join-Path $repository 'artifacts\rtx3070'
$destination = Join-Path $outputRoot $packageName

& (Join-Path $PSScriptRoot 'publish-mvp.ps1') -OutputDirectory $destination -NoRestore:$NoRestore

foreach ($component in 'MpvShell.App', 'MpvShell.Player.Abstractions', 'MpvShell.Player.LibMpv', 'MpvShell.Rendering.WinUI') {
    if (-not (Test-Path -LiteralPath (Join-Path $destination ($component + '.pdb')) -PathType Leaf)) {
        throw "发布包缺少托管调试符号：$component.pdb"
    }
}

# 仓库源文件保持 UTF-8 无 BOM；便携脚本添加 BOM，让目标机 Windows PowerShell 5.1 正确读取中文。
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'portable') -File) {
    $targetPath = Join-Path $destination $file.Name
    if ($file.Extension -eq '.ps1') {
        [IO.File]::WriteAllText($targetPath, [IO.File]::ReadAllText($file.FullName), [Text.UTF8Encoding]::new($true))
    }
    else { Copy-Item -LiteralPath $file.FullName -Destination $targetPath }
}

$mediaFiles = @()
if ($IncludeTestMedia) {
    $sourceMedia = Join-Path $repository 'artifacts\hdr-4k\media'
    $mediaManifest = Get-Content -LiteralPath (Join-Path $sourceMedia 'media-manifest.json') -Raw | ConvertFrom-Json
    $mediaDestination = Join-Path $destination 'TestMedia'
    [void] [IO.Directory]::CreateDirectory($mediaDestination)
    foreach ($name in 'hevc-main10-hdr10-4k30.mkv', 'av1-main10-sdr-4k30.mkv', 'pq-gradient-10bit-4k.mkv') {
        $sourcePath = Join-Path $sourceMedia $name
        $asset = @($mediaManifest.samples | Where-Object { $_.fileName -eq $name })
        $hash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        if ($asset.Count -ne 1 -or $hash -ne $asset[0].sha256) { throw "测试素材清单/哈希不匹配：$name" }
        Copy-Item -LiteralPath $sourcePath -Destination $mediaDestination
        $mediaFiles += [ordered]@{ fileName = $name; sha256 = $hash; bytes = (Get-Item -LiteralPath $sourcePath).Length }
    }
    Copy-Item -LiteralPath (Join-Path $sourceMedia 'media-manifest.json') -Destination $mediaDestination
    Copy-Item -LiteralPath (Join-Path $sourceMedia 'media-verification.json') -Destination $mediaDestination
}

$gitCommit = & git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw '无法记录源码提交。' }
$gitStatus = @(& git -C $repository status --porcelain)
if ($LASTEXITCODE -ne 0) { throw '无法记录源码状态。' }
$sourceFiles = @(& git -C $repository -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw '无法枚举源码。' }
$sourceHashes = @($sourceFiles | Sort-Object -Unique | ForEach-Object {
    $sourcePath = Join-Path $repository $_
    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
        [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash }
    }
})
$sourceHashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'source-hashes.json') -Encoding utf8NoBOM
$buildInfo = [ordered]@{
    schemaVersion = 1
    packageId = $packageId
    packageName = $packageName
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    gitCommit = "$gitCommit"
    sourceDirty = $gitStatus.Count -gt 0
    sourceStatus = $gitStatus
    sourceHashesSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'source-hashes.json') -Algorithm SHA256).Hash
    sdkVersion = (& dotnet --version)
    configuration = 'Release'
    rid = 'win-x64'
    selfContained = $true
    windowsAppSdkSelfContained = $true
    logLevel = 'debug'
    launchScript = 'Start-Debug.cmd'
    collectScript = 'Collect-Logs.cmd'
    appDllSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'MpvShell.App.dll') -Algorithm SHA256).Hash
    libMpvManagedDllSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'MpvShell.Player.LibMpv.dll') -Algorithm SHA256).Hash
    testMedia = $mediaFiles
}
$buildInfo | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'build-info.json') -Encoding utf8NoBOM

$zipPath = $destination + '.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($destination, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
"$zipHash  $packageName.zip" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ascii
[pscustomobject]@{
    PackageDirectory = $destination
    ZipPath = $zipPath
    ZipBytes = (Get-Item -LiteralPath $zipPath).Length
    Sha256 = $zipHash
    PackageId = $packageId
} | Format-List

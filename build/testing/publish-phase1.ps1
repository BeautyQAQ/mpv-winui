#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $HdrMediaDirectory,
    [Parameter(Mandatory)][string] $P104MediaDirectory,
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$packageId = [Guid]::NewGuid().ToString('N')
$packageName = 'MpvShell-phase1-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $packageId.Substring(0, 6)
$outputRoot = Join-Path $repository 'artifacts/phase-1'
$destination = Join-Path $outputRoot $packageName
[void][IO.Directory]::CreateDirectory($outputRoot)

# 在构建前冻结全部 Git 管理及未忽略的新文件；产物目录不会混入源码。
$gitCommit = & git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw '无法记录源码提交。' }
$gitStatus = @(& git -C $repository status --porcelain)
if ($LASTEXITCODE -ne 0) { throw '无法记录工作区状态。' }
$sourceFiles = @(& git -C $repository -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw '无法枚举源码。' }
$sourceHashes = @($sourceFiles | Sort-Object -Unique | ForEach-Object {
    $path = Join-Path $repository $_
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        [ordered]@{ path = $_; bytes = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path).Hash }
    }
})
$sourceArchive = Join-Path $outputRoot ($packageName + '-source.zip')
$archive = [IO.Compression.ZipFile]::Open($sourceArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $sourceHashes) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $repository $file.path), $file.path)
    }
}
finally { $archive.Dispose() }

& (Join-Path $PSScriptRoot 'publish-mvp.ps1') -OutputDirectory $destination -NoRestore:$NoRestore
$currentSourceFiles = @(& git -C $repository -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0 -or (Compare-Object @($sourceFiles | Sort-Object -Unique) @($currentSourceFiles | Sort-Object -Unique))) {
    throw '构建期间源码文件集合发生变化，不能封包。'
}
foreach ($file in $sourceHashes) {
    if ((Get-FileHash -LiteralPath (Join-Path $repository $file.path)).Hash -ne $file.sha256) {
        throw "构建期间源码发生变化，不能封包：$($file.path)"
    }
}
# 底层脚本在临时发布目录做烟雾测试；搬入候选目录后再次实际创建/销毁会话。
[MpvPublishSessionSmoke]::Run((Join-Path $destination 'runtimes/win-x64/native/libmpv-2.dll'))
foreach ($component in 'MpvShell.App', 'MpvShell.Player.Abstractions', 'MpvShell.Player.LibMpv', 'MpvShell.Rendering.WinUI') {
    if (-not (Test-Path -LiteralPath (Join-Path $destination ($component + '.pdb')) -PathType Leaf)) {
        throw "发布包缺少调试符号：$component.pdb"
    }
}

# 源文件无 BOM，发布后的 PS1 添加 BOM 兼容系统 Windows PowerShell 5.1。
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'portable') -File) {
    $contents = [IO.File]::ReadAllText($file.FullName).Replace('RTX 3070 外机测试', 'Phase 1 发布验证').Replace('MpvShell-RTX3070-Logs-', 'MpvShell-Phase1-Logs-')
    [IO.File]::WriteAllText((Join-Path $destination $file.Name), $contents, [Text.UTF8Encoding]::new($file.Extension -eq '.ps1'))
}
@'
# Phase 1 发布包

Windows x64 Release 自包含包，最低系统 Windows 10 1809。完整解压后运行 MpvShell.App.exe；无需安装 SDK 或使用管理员权限。

要记录详细日志，将媒体拖到 Start-Debug.cmd，或运行 Start-Debug.ps1 -MediaPath <绝对路径>。Start-Debug 表示日志级别，应用仍为 Release。
关闭播放器并等待退出码后，运行 Collect-Logs.cmd；它生成 MpvShell-Phase1-Logs-*.zip，不删除原始日志。Test-Notes.txt 用于记录复现步骤。
日志包含媒体路径和系统/显卡信息；收集仅生成本地文件。

TestMedia/4k 包含 HEVC HDR10、AV1 SDR、PQ 渐变短样片；TestMedia/p1-04 包含多音轨/SRT/ASS、纯音频、纯视频和本机 HLS 文件。
这些是功能验证输入。4K 全屏流畅度、HDR 观感和长期稳定性需要按显卡、显示器、Windows HDR 状态及输出表面分别记录。

build-info.json 记录基线提交、工作区状态、源码快照身份及工具版本；source-hashes.json 对应同名 -source.zip（位于本 ZIP 旁）。
原生锁定版本、补丁、构建步骤及许可证见 SourceBuild/ 和 ThirdParty/。构建脚本须在完整源码快照的 build/native/ 内运行。
不保证重新构建得到逐字节相同的二进制。验收状态以仓库 docs/implementation/release-status.md 为准。
'@ | Set-Content -LiteralPath (Join-Path $destination 'README-测试说明.md') -Encoding utf8NoBOM

$mediaFiles = @()
foreach ($group in @(
    @{ Root = [IO.Path]::GetFullPath($HdrMediaDirectory); Label = '4k'; Key = 'samples'; Name = 'fileName' },
    @{ Root = [IO.Path]::GetFullPath($P104MediaDirectory); Label = 'p1-04'; Key = 'files'; Name = 'name' }
)) {
    $manifestPath = Join-Path $group.Root 'media-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $targetRoot = Join-Path $destination ('TestMedia/' + $group.Label)
    [void][IO.Directory]::CreateDirectory($targetRoot)
    foreach ($asset in $manifest.($group.Key)) {
        $name = $asset.($group.Name)
        $source = [IO.Path]::GetFullPath((Join-Path $group.Root $name))
        if (-not $source.StartsWith($group.Root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "素材路径越界：$name" }
        $hash = (Get-FileHash -LiteralPath $source).Hash
        if ($hash -ne $asset.sha256) { throw "素材哈希不匹配：$name" }
        $target = Join-Path $targetRoot $name
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        Copy-Item -LiteralPath $source -Destination $target
        $mediaFiles += [ordered]@{ path = 'TestMedia/' + $group.Label + '/' + $name; sha256 = $hash }
    }
    Copy-Item -LiteralPath $manifestPath -Destination $targetRoot
    if (Test-Path -LiteralPath (Join-Path $group.Root 'media-verification.json')) {
        Copy-Item -LiteralPath (Join-Path $group.Root 'media-verification.json') -Destination $targetRoot
    }
}
Copy-Item -LiteralPath (Join-Path $repository 'build/native') -Destination (Join-Path $destination 'SourceBuild') -Recurse
& (Join-Path $PSScriptRoot 'copy-managed-licenses.ps1') -PackageDirectory $destination
$sourceHashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'source-hashes.json') -Encoding utf8NoBOM
$buildInfo = [ordered]@{
    schemaVersion = 2; packageId = $packageId; packageName = $packageName
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); gitCommit = "$gitCommit"
    sourceDirty = $gitStatus.Count -gt 0; sourceStatus = $gitStatus
    sourceHashesSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'source-hashes.json')).Hash
    sourceArchive = [IO.Path]::GetFileName($sourceArchive); sourceArchiveSha256 = (Get-FileHash -LiteralPath $sourceArchive).Hash
    sdkVersion = (& dotnet --version); powerShellVersion = $PSVersionTable.PSVersion.ToString()
    configuration = 'Release'; rid = 'win-x64'; selfContained = $true; windowsAppSdkSelfContained = $true
    finalDirectorySessionSmokeCycles = 3
    logLevel = 'debug'; launchScript = 'Start-Debug.cmd'; collectScript = 'Collect-Logs.cmd'
    appDllSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'MpvShell.App.dll')).Hash
    libMpvManagedDllSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'MpvShell.Player.LibMpv.dll')).Hash
    nativeSourceLockSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'SourceBuild/source-lock.json')).Hash
    nugetLicenseOriginsSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'ThirdParty/nuget-license-origins.json')).Hash
    testMedia = $mediaFiles
}
$buildInfo | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'build-info.json') -Encoding utf8NoBOM
"$($buildInfo.sourceArchiveSha256)  $($buildInfo.sourceArchive)" | Set-Content -LiteralPath ($sourceArchive + '.sha256') -Encoding ascii
Write-Host "待验收目录：$destination"
Write-Host '请从此目录验证启动、播放和正常退出，将运行日志保存到目录外，再用 seal-phase1-package.ps1 校验并封包。'

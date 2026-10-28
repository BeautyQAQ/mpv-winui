#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($PackageDirectory))
$artifactPrefix = (Join-Path $repository 'artifacts') + [IO.Path]::DirectorySeparatorChar
if (-not $root.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '只能封装仓库 artifacts 内的候选目录。' }
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '候选目录不能是目录联接。' }
if (Test-Path -LiteralPath (Join-Path $root 'logs')) { throw '先将本机验收日志保存在包外，再封包。' }
if (Test-Path -LiteralPath ($root + '.zip')) { throw '已有 ZIP 不覆盖，请使用新的候选目录。' }
$build = Get-Content -LiteralPath (Join-Path $root 'build-info.json') -Raw | ConvertFrom-Json
if ([IO.Path]::GetFileName($root) -ne $build.packageName) { throw '目录名与包身份不匹配。' }

function Assert-Hash([string] $Path, [string] $Expected) {
    if ((Get-FileHash -LiteralPath $Path).Hash -ne $Expected) { throw "哈希不匹配：$Path" }
}
function Assert-Zip([string] $Path, [object[]] $Files, [string] $Prefix = '') {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($zip.Entries | Where-Object { $_.Name })
        if ($entries.Count -ne $Files.Count) { throw "ZIP 文件数不匹配：$Path" }
        foreach ($file in $Files) {
            $entry = $zip.GetEntry($Prefix + $file.path.Replace('\', '/'))
            if (-not $entry -or $entry.Length -ne $file.bytes) { throw "ZIP 条目缺失或长度不匹配：$($file.path)" }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne $file.sha256) { throw "ZIP 条目哈希不匹配：$($file.path)" }
        }
    }
    finally { $zip.Dispose() }
}
function Get-ContentHashes {
    @(Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
}

Assert-Hash (Join-Path $root 'source-hashes.json') $build.sourceHashesSha256
Assert-Hash (Join-Path $root 'MpvShell.App.dll') $build.appDllSha256
Assert-Hash (Join-Path $root 'MpvShell.Player.LibMpv.dll') $build.libMpvManagedDllSha256
$published = Get-Content -LiteralPath (Join-Path $root 'publish-verification.json') -Raw | ConvertFrom-Json
Assert-Hash (Join-Path $root 'MpvShell.App.exe') $published.executableSha256
Assert-Hash (Join-Path $root 'SourceBuild/source-lock.json') $build.nativeSourceLockSha256
$source = Get-Content -LiteralPath (Join-Path $root 'source-hashes.json') -Raw | ConvertFrom-Json
$sourceArchive = Join-Path ([IO.Path]::GetDirectoryName($root)) $build.sourceArchive
Assert-Hash $sourceArchive $build.sourceArchiveSha256
Assert-Zip $sourceArchive $source
foreach ($file in $source | Where-Object { $_.path.StartsWith('build/native/') -or $_.path.StartsWith('src/MpvShell.App/Assets/ThirdParty/') }) {
    $target = if ($file.path.StartsWith('build/native/')) { 'SourceBuild/' + $file.path.Substring(13) }
              else { 'ThirdParty/' + $file.path.Substring('src/MpvShell.App/Assets/ThirdParty/'.Length) }
    Assert-Hash (Join-Path $root $target) $file.sha256
}
$origins = Get-Content -LiteralPath (Join-Path $root 'ThirdParty/license-origins.json') -Raw | ConvertFrom-Json
foreach ($file in $origins.files) { Assert-Hash (Join-Path $root ('ThirdParty/' + $file.path)) $file.sha256 }
Assert-Hash (Join-Path $root 'ThirdParty/nuget-license-origins.json') $build.nugetLicenseOriginsSha256
$nugetOrigins = Get-Content -LiteralPath (Join-Path $root 'ThirdParty/nuget-license-origins.json') -Raw | ConvertFrom-Json
foreach ($package in $nugetOrigins.packages) {
    foreach ($file in $package.files) { Assert-Hash (Join-Path $root $file.path) $file.sha256 }
}
foreach ($file in $build.testMedia) { Assert-Hash (Join-Path $root $file.path) $file.sha256 }
$nativeRoot = Join-Path $root 'runtimes/win-x64/native'
$manifestPath = Join-Path $nativeRoot 'native-dependencies.json'
$lockedManifest = @($source | Where-Object path -eq 'src/MpvShell.Player.LibMpv/Native/native-dependencies.lock.json')
if ($lockedManifest.Count -ne 1) { throw '源码快照缺少原生清单。' }
Assert-Hash $manifestPath $lockedManifest[0].sha256
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (Compare-Object @($manifest.assets.fileName | Sort-Object) @(Get-ChildItem -LiteralPath $nativeRoot -Filter '*.dll' -File | Select-Object -ExpandProperty Name | Sort-Object)) {
    throw '原生 DLL 集合与清单不符。'
}
foreach ($name in 'mpv.exe', 'MpvShell.Player.MpvSidecar.dll', 'MpvShell.Interop.VideoHost.dll') {
    if (Get-ChildItem -LiteralPath $root -Recurse -File -Filter $name) { throw "包内有旧路线文件：$name" }
}
& (Join-Path $root 'SourceBuild/test-native-closure.ps1') -NativeDirectory $nativeRoot -ManifestPath $manifestPath
$verification = [ordered]@{
    verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); packageId = $build.packageId
    sourceArchiveVerifiedFiles = @($source).Count; licenseOriginFiles = @($origins.files).Count
    nugetLicensePackages = @($nugetOrigins.packages).Count
    testMediaFiles = @($build.testMedia).Count; nativeAssets = @($manifest.assets).Count
    sourceBuildAndNoticesMatchSnapshot = $true; nativeLoadAndApiPassed = $true
    note = '此记录仅证明内容一致性；GUI 播放/退出及阶段验收证据单独登记。'
}
$verification | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'package-verification.json') -Encoding utf8NoBOM
# 内容清单排除自身；ZIP 条目校验另外包含该清单，外部 SHA-256 覆盖整个 ZIP。
$contentHashes = @(Get-ContentHashes | Where-Object path -ne 'package-content-sha256.json')
$contentHashes | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $root 'package-content-sha256.json') -Encoding utf8NoBOM
$files = Get-ContentHashes
$zipPath = $root + '.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($root, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
Assert-Zip $zipPath $files ($build.packageName + '/')
$hash = (Get-FileHash -LiteralPath $zipPath).Hash
"$hash  $($build.packageName).zip" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ascii
$result = [ordered]@{
    packageName = $build.packageName; packageId = $build.packageId; zipPath = $zipPath
    bytes = (Get-Item -LiteralPath $zipPath).Length; sha256 = $hash; verifiedEntries = $files.Count
    sourceArchive = $sourceArchive; sourceArchiveSha256 = $build.sourceArchiveSha256
}
$result | ConvertTo-Json | Set-Content -LiteralPath ($root + '.verification.json') -Encoding utf8NoBOM
$result | Format-List

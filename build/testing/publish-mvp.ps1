#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\mvp\win-x64'),
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts'))
$artifactPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$destination = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($OutputDirectory))

function Assert-ArtifactPath([string] $Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "发布和备份路径必须位于仓库 artifacts 子目录：$absolute"
    }
    if (Test-Path -LiteralPath $absolute) {
        $item = Get-Item -LiteralPath $absolute -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "发布路径不能是符号链接或目录联接：$absolute"
        }
    }
}

function Assert-X64Pe([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "不是 PE 文件：$Path" }
        $stream.Position = 0x3C
        $headerOffset = $reader.ReadUInt32()
        $stream.Position = $headerOffset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) {
            throw "发布文件不是 x64 PE：$Path"
        }
    }
    finally { $reader.Dispose() }
}

Assert-ArtifactPath $destination
$parent = [IO.Path]::GetDirectoryName($destination)
[void] [IO.Directory]::CreateDirectory($parent)
$staging = Join-Path $parent ('.publish-' + [Guid]::NewGuid().ToString('N'))
Assert-ArtifactPath $staging
[void] [IO.Directory]::CreateDirectory($staging)

# 构建/测试以 mpv-winui.slnx 为入口；发布只选择应用，避免把测试程序集混入便携目录。
$project = Join-Path $repository 'src\MpvShell.App\MpvShell.App.csproj'
$arguments = @(
    'publish', $project, '-c', 'Release', '-p:Platform=x64', '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishTrimmed=false', '-o', $staging
)
if ($NoRestore) { $arguments += '--no-restore' }

$previousHttpProxy = $env:HTTP_PROXY
$previousHttpsProxy = $env:HTTPS_PROXY
try {
    # 只对本次发布/还原设置代理，不修改用户全局环境。
    $env:HTTP_PROXY = 'http://127.0.0.1:7890'
    $env:HTTPS_PROXY = 'http://127.0.0.1:7890'
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Release 自包含发布失败，退出码 $LASTEXITCODE。中间目录：$staging" }
}
finally {
    $env:HTTP_PROXY = $previousHttpProxy
    $env:HTTPS_PROXY = $previousHttpsProxy
}

$requiredFiles = @(
    'MpvShell.App.exe', 'MpvShell.App.dll', 'MpvShell.App.runtimeconfig.json',
    'MpvShell.Player.LibMpv.dll', 'MpvShell.Rendering.WinUI.dll',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.UI.Xaml.dll', 'MpvShell.App.pri'
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $staging $relativePath) -PathType Leaf)) {
        throw "自包含发布缺少必要文件：$relativePath"
    }
}
foreach ($legacyFile in 'mpv.exe', 'MpvShell.Player.MpvSidecar.dll', 'MpvShell.Interop.VideoHost.dll') {
    if (Get-ChildItem -LiteralPath $staging -Recurse -File -Filter $legacyFile) {
        throw "发布目录包含已废弃播放路线：$legacyFile"
    }
}

$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $staging 'MpvShell.App.runtimeconfig.json') | ConvertFrom-Json
if (-not ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'includedFrameworks') -or
    -not ($runtimeConfig.runtimeOptions.includedFrameworks.name -contains 'Microsoft.NETCore.App')) {
    throw 'runtimeconfig 没有声明内置 .NET 运行时，不能认定为自包含发布。'
}

$nativeDirectory = Join-Path $staging 'runtimes\win-x64\native'
$publishedManifest = Join-Path $nativeDirectory 'native-dependencies.json'
$sourceManifest = Join-Path $repository 'src\MpvShell.Player.LibMpv\Native\native-dependencies.lock.json'
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $publishedManifest).Hash -ne
    (Get-FileHash -Algorithm SHA256 -LiteralPath $sourceManifest).Hash) {
    throw '发布原生清单与仓库锁定清单不一致。'
}
$manifest = Get-Content -Raw -LiteralPath $publishedManifest | ConvertFrom-Json
$actualNativeNames = @(Get-ChildItem -LiteralPath $nativeDirectory -File -Filter '*.dll' | Select-Object -ExpandProperty Name)
if (Compare-Object @($manifest.assets.fileName | Sort-Object) @($actualNativeNames | Sort-Object)) {
    throw '发布原生 DLL 集合与锁定清单不一致。'
}
Assert-X64Pe (Join-Path $staging 'MpvShell.App.exe')
foreach ($asset in $manifest.assets) { Assert-X64Pe (Join-Path $nativeDirectory $asset.fileName) }

# 烟雾测试从实际发布目录逐项校验 SHA-256、加载 DLL，并检查 Client API；不启动 GUI。
& (Join-Path $repository 'build\native\test-native-closure.ps1') `
    -NativeDirectory $nativeDirectory -ManifestPath $publishedManifest

if (-not ('MpvPublishSessionSmoke' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class MpvPublishSessionSmoke
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize(IntPtr core);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(IntPtr core);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetOption(
        IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    private static T Export<T>(IntPtr library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    public static void Run(string libraryPath)
    {
        IntPtr library = NativeLibrary.Load(libraryPath);
        try
        {
            var create = Export<Create>(library, "mpv_create");
            var initialize = Export<Initialize>(library, "mpv_initialize");
            var destroy = Export<Destroy>(library, "mpv_terminate_destroy");
            var setOption = Export<SetOption>(library, "mpv_set_option_string");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                IntPtr core = create();
                if (core == IntPtr.Zero) throw new InvalidOperationException("发布版 mpv_create 失败。");
                try
                {
                    foreach (var option in new[] {
                        new[] { "config", "no" }, new[] { "load-scripts", "no" },
                        new[] { "osc", "no" }, new[] { "ytdl", "no" },
                        new[] { "input-default-bindings", "no" },
                        new[] { "vo", "libmpv" }, new[] { "ao", "null" }
                    })
                    {
                        int result = setOption(core, option[0], option[1]);
                        // 锁定的 libmpv 不含脚本支持；与生产会话一致，允许这些选项不存在。
                        if (result == -5 && option[0] is "load-scripts" or "osc" or "ytdl") continue;
                        if (result < 0) throw new InvalidOperationException("发布版设置选项失败：" + option[0] + "，错误 " + result);
                    }
                    int status = initialize(core);
                    if (status < 0) throw new InvalidOperationException("发布版 mpv_initialize 失败，错误 " + status);
                }
                finally { destroy(core); }
            }
        }
        finally { NativeLibrary.Free(library); }
    }
}
'@
}
[MpvPublishSessionSmoke]::Run((Join-Path $nativeDirectory 'libmpv-2.dll'))
Write-Host 'MPV SESSION PASS  3 次创建、初始化与销毁'

$files = @(Get-ChildItem -LiteralPath $staging -Recurse -File)
$nativeAssets = @($manifest.assets | ForEach-Object {
    $assetPath = Join-Path $nativeDirectory $_.fileName
    [ordered]@{ fileName = $_.fileName; bytes = (Get-Item -LiteralPath $assetPath).Length; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $assetPath).Hash; peMachine = 'AMD64' }
})
$report = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    configuration = 'Release'
    rid = 'win-x64'
    selfContained = $true
    windowsAppSdkSelfContained = $true
    executable = 'MpvShell.App.exe'
    executableBytes = (Get-Item -LiteralPath (Join-Path $staging 'MpvShell.App.exe')).Length
    executableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $staging 'MpvShell.App.exe')).Hash
    fileCountBeforeReport = $files.Count
    totalBytesBeforeReport = ($files | Measure-Object -Property Length -Sum).Sum
    nativeManifestMatchesLock = $true
    nativeLoadSmokePassed = $true
    nativeSessionSmokeCycles = 3
    nativeAssets = $nativeAssets
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $staging 'publish-verification.json') -Encoding utf8NoBOM

# 验证成功才替换最终目录；保留原输出作为备份，不删除用户已有产物。
$previousOutput = $null
if (Test-Path -LiteralPath $destination) {
    $previousOutput = $destination + '.previous-' + [Guid]::NewGuid().ToString('N')
    Assert-ArtifactPath $destination
    Assert-ArtifactPath $previousOutput
    Move-Item -LiteralPath $destination -Destination $previousOutput
}
Assert-ArtifactPath $staging
Assert-ArtifactPath $destination
Move-Item -LiteralPath $staging -Destination $destination

[pscustomobject]@{
    Executable = Join-Path $destination 'MpvShell.App.exe'
    ExecutableBytes = $report.executableBytes
    TotalBytes = $report.totalBytesBeforeReport
    NativeAssetsVerified = $nativeAssets.Count
    VerificationReport = Join-Path $destination 'publish-verification.json'
    PreviousOutput = $previousOutput
} | Format-List

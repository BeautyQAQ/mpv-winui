[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourcePath,

    [Parameter(Mandatory)]
    [string] $NativeOutputPath,

    [string] $VsInstallPath = 'C:\Program Files\Microsoft Visual Studio\18\Community',

    [Parameter(Mandatory)]
    [string] $LlvmBinPath,

    [Parameter(Mandatory)]
    [string] $NinjaPath,

    [string] $PythonPath = 'C:\Program Files\Python314\python.exe',

    [string] $PythonPackagesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-NativeSuccess([string] $Operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation 失败，退出码：$LASTEXITCODE"
    }
}

function Sync-GitSource(
    [string] $Repository,
    [string] $Commit,
    [string] $Destination,
    [string] $Name
) {
    if (-not (Test-Path -LiteralPath (Join-Path $Destination '.git'))) {
        git clone --filter=blob:none $Repository $Destination
        Assert-NativeSuccess "克隆 $Name"
    }

    git -C $Destination fetch --depth 1 origin $Commit
    Assert-NativeSuccess "获取 $Name 锁定 commit"
    git -C $Destination checkout --detach $Commit
    Assert-NativeSuccess "检出 $Name 锁定 commit"

    $actual = (git -C $Destination rev-parse HEAD).Trim()
    Assert-NativeSuccess "读取 $Name commit"
    if ($actual -ne $Commit) {
        throw "$Name commit 不匹配。预期 $Commit，实际 $actual。"
    }
}

function Apply-LockedPatch(
    [string] $RepositoryPath,
    [string] $PatchPath,
    [string] $Name
) {
    git -C $RepositoryPath apply --check $PatchPath 2>$null
    if ($LASTEXITCODE -eq 0) {
        git -C $RepositoryPath apply $PatchPath
        Assert-NativeSuccess "应用 $Name 补丁"
    }
    else {
        git -C $RepositoryPath apply --reverse --check $PatchPath 2>$null
        Assert-NativeSuccess "验证 $Name 补丁已应用"
    }
}

$lockPath = Join-Path $PSScriptRoot 'source-lock.json'
$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
$source = [IO.Path]::GetFullPath($SourcePath)
$output = [IO.Path]::GetFullPath($NativeOutputPath)
$llvmBin = [IO.Path]::GetFullPath($LlvmBinPath)
$ninja = [IO.Path]::GetFullPath($NinjaPath)
$python = [IO.Path]::GetFullPath($PythonPath)

foreach ($path in $llvmBin, $ninja, $python) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "构建工具路径不存在：$path"
    }
}

$devShellModule = Join-Path $VsInstallPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
if (-not (Test-Path -LiteralPath $devShellModule -PathType Leaf)) {
    throw "Visual Studio DevShell 模块不存在：$devShellModule"
}

Import-Module $devShellModule
Enter-VsDevShell -VsInstallPath $VsInstallPath -SkipAutomaticLocation `
    -DevCmdArguments '-arch=x64 -host_arch=x64'
$env:Path = "$llvmBin;$env:Path"
$env:CC = 'clang-cl'
$env:CXX = 'clang-cl'
$env:CC_LD = 'lld-link'
$env:CXX_LD = 'lld-link'
if ($PythonPackagesPath) {
    $env:PYTHONPATH = [IO.Path]::GetFullPath($PythonPackagesPath)
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw '缺少构建工具：git'
}

Sync-GitSource $lock.mpv.repository $lock.mpv.commit $source 'mpv'
Apply-LockedPatch $source `
    (Join-Path $PSScriptRoot 'patches\mpv-msvc-rc-codepage.patch') `
    'mpv MSVC rc.exe UTF-8 codepage'

$subprojects = Join-Path $source 'subprojects'
New-Item -ItemType Directory -Force -Path $subprojects | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'mpv-subprojects') -Filter '*.wrap' -File |
    Copy-Item -Destination $subprojects -Force

$dependencySources = @(
    @{
        Name = 'FFmpeg'
        Repository = 'https://github.com/FFmpeg/FFmpeg.git'
        Commit = $lock.mpv.dependencies.ffmpeg.commit
        Path = Join-Path $subprojects 'ffmpeg'
    },
    @{
        Name = 'libass'
        Repository = 'https://github.com/libass/libass.git'
        Commit = $lock.mpv.dependencies.libass.commit
        Path = Join-Path $subprojects 'libass'
    },
    @{
        Name = 'libplacebo'
        Repository = 'https://code.videolan.org/videolan/libplacebo.git'
        Commit = $lock.mpv.dependencies.libplacebo.commit
        Path = Join-Path $subprojects 'libplacebo'
    }
)

foreach ($dependency in $dependencySources) {
    Sync-GitSource $dependency.Repository $dependency.Commit $dependency.Path $dependency.Name
}

Apply-LockedPatch $dependencySources[0].Path `
    (Join-Path $PSScriptRoot 'patches\ffmpeg-clang-cl-dumpbin.patch') `
    'FFmpeg clang-cl 符号工具'
Apply-LockedPatch $dependencySources[1].Path `
    (Join-Path $PSScriptRoot 'patches\libass-clang-cl-bitscan.patch') `
    'libass clang-cl _BitScanReverse 类型'

$libplaceboPath = $dependencySources[2].Path
$requiredSubmodules = @(
    '3rdparty/Vulkan-Headers',
    '3rdparty/fast_float',
    '3rdparty/glad',
    '3rdparty/jinja',
    '3rdparty/markupsafe'
)
git -C $libplaceboPath submodule update --init --depth 1 -- @requiredSubmodules
Assert-NativeSuccess '初始化 libplacebo 构建子模块'

$vulkanHeadersCommit = (git -C (Join-Path $libplaceboPath '3rdparty\Vulkan-Headers') rev-parse HEAD).Trim()
Assert-NativeSuccess '读取 Vulkan-Headers commit'
if ($vulkanHeadersCommit -ne $lock.mpv.dependencies.vulkanHeaders.commit) {
    throw "Vulkan-Headers commit 不匹配。预期 $($lock.mpv.dependencies.vulkanHeaders.commit)，实际 $vulkanHeadersCommit。"
}

$buildDirectory = Join-Path $source 'build-win-x64-release'
$options = @($lock.mpv.mesonOptions)
$setupArgs = @('-m', 'mesonbuild.mesonmain', 'setup')
if (Test-Path -LiteralPath $buildDirectory -PathType Container) {
    $setupArgs += '--wipe'
}
$setupArgs += @($buildDirectory, $source)
$setupArgs += $options

& $python @setupArgs
Assert-NativeSuccess '配置 mpv 与锁定的静态依赖闭包'
& $ninja -C $buildDirectory 'mpv-2.dll'
Assert-NativeSuccess '构建 mpv-2.dll'

$candidate = Join-Path $buildDirectory 'mpv-2.dll'
if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
    throw "构建完成但未找到 mpv-2.dll：$candidate"
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
Copy-Item -LiteralPath $candidate -Destination (Join-Path $output 'libmpv-2.dll') -Force

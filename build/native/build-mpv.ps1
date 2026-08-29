[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourcePath,

    [Parameter(Mandatory)]
    [string] $DependencyPrefix,

    [Parameter(Mandatory)]
    [string] $NativeOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lockPath = Join-Path $PSScriptRoot 'source-lock.json'
$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
$source = [IO.Path]::GetFullPath($SourcePath)
$prefix = [IO.Path]::GetFullPath($DependencyPrefix)
$output = [IO.Path]::GetFullPath($NativeOutputPath)

foreach ($toolName in 'git', 'meson', 'ninja') {
    if (-not (Get-Command $toolName -ErrorAction SilentlyContinue)) {
        throw "缺少构建工具：$toolName"
    }
}

if (-not (Test-Path -LiteralPath $prefix -PathType Container)) {
    throw "依赖前缀不存在：$prefix。必须先按锁定版本准备 FFmpeg、libplacebo、libass 等 LGPL 兼容依赖闭包。"
}

if (-not (Test-Path -LiteralPath (Join-Path $source '.git'))) {
    git clone --filter=blob:none $lock.mpv.repository $source
}

git -C $source fetch --depth 1 origin $lock.mpv.commit
git -C $source checkout --detach $lock.mpv.commit

$env:CMAKE_PREFIX_PATH = $prefix
$env:PKG_CONFIG_PATH = Join-Path $prefix 'lib\pkgconfig'
$buildDirectory = Join-Path $source 'build-win-x64-release'
$options = @($lock.mpv.mesonOptions)

if (Test-Path -LiteralPath $buildDirectory -PathType Container) {
    meson setup $buildDirectory $source --wipe @options
}
else {
    meson setup $buildDirectory $source @options
}
ninja -C $buildDirectory libmpv

$candidate = Get-ChildItem -LiteralPath $buildDirectory -Recurse -Filter 'libmpv-2.dll' -File |
    Select-Object -First 1
if (-not $candidate) {
    throw "构建完成但未找到 libmpv-2.dll。"
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
Copy-Item -LiteralPath $candidate.FullName -Destination $output

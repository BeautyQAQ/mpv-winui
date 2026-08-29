[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $DepotToolsPath,

    [Parameter(Mandatory)]
    [string] $SourcePath,

    [Parameter(Mandatory)]
    [string] $NativeOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lockPath = Join-Path $PSScriptRoot 'source-lock.json'
$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
$source = [IO.Path]::GetFullPath($SourcePath)
$output = [IO.Path]::GetFullPath($NativeOutputPath)
$depotTools = [IO.Path]::GetFullPath($DepotToolsPath)
$python = Join-Path $depotTools 'python3.bat'
$gclient = Join-Path $depotTools 'gclient.bat'

foreach ($tool in $python, $gclient) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        throw "缺少 depot_tools 工具：$tool"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $source '.git'))) {
    git clone --filter=blob:none $lock.angle.repository $source
}

git -C $source fetch --depth 1 origin $lock.angle.commit
git -C $source checkout --detach $lock.angle.commit

$depotToolsOriginalPath = $env:PATH
$env:PATH = "$depotTools;$depotToolsOriginalPath"
Push-Location $source
try {
    & $python scripts/bootstrap.py
    & $gclient sync --revision "angle@$($lock.angle.commit)" --no-history

    $gn = Get-Command 'gn' -ErrorAction SilentlyContinue
    $autoninja = Get-Command 'autoninja' -ErrorAction SilentlyContinue
    if (-not $gn -or -not $autoninja) {
        throw "gclient sync 后仍缺少 gn 或 autoninja；请检查 depot_tools bootstrap/CIPD 输出。"
    }

    $actualDepsHash = (Get-FileHash -Algorithm SHA256 -LiteralPath 'DEPS').Hash
    if ($actualDepsHash -ne $lock.angle.depsSha256) {
        throw "ANGLE DEPS 哈希不匹配：$actualDepsHash"
    }

    $buildDirectory = Join-Path $source 'out\ReleaseD3D11'
    $args = @'
is_debug = false
is_component_build = false
target_cpu = "x64"
angle_build_all = false
angle_build_tests = false
angle_enable_d3d11 = true
angle_enable_d3d9 = false
angle_enable_gl = false
angle_enable_null = false
angle_enable_vulkan = false
angle_enable_swiftshader = false
'@

    New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $buildDirectory 'args.gn') -Value $args -Encoding utf8NoBOM
    & $gn.Source gen $buildDirectory
    & $autoninja.Source -C $buildDirectory libEGL libGLESv2

    New-Item -ItemType Directory -Force -Path $output | Out-Null
    Copy-Item -LiteralPath (Join-Path $buildDirectory 'libEGL.dll') -Destination $output
    Copy-Item -LiteralPath (Join-Path $buildDirectory 'libGLESv2.dll') -Destination $output
}
finally {
    Pop-Location
    $env:PATH = $depotToolsOriginalPath
}

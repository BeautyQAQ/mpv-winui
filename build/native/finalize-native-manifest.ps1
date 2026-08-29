[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $NativeDirectory,

    [string] $ManifestPath = (Join-Path $PSScriptRoot '..\..\src\MpvShell.Player.LibMpv\Native\native-dependencies.lock.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$directory = [IO.Path]::GetFullPath($NativeDirectory)
$manifestFile = [IO.Path]::GetFullPath($ManifestPath)
$manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json
$registered = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($asset in $manifest.assets) {
    $path = Join-Path $directory $asset.fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "缺少清单资产：$path"
    }

    [void] $registered.Add($asset.fileName)
    $asset.sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
}

$unregistered = Get-ChildItem -LiteralPath $directory -File -Filter '*.dll' |
    Where-Object { -not $registered.Contains($_.Name) }
if ($unregistered) {
    throw "发现未登记 DLL：$($unregistered.Name -join ', ')。请先审计来源、许可证、分组和加载顺序，再加入清单。"
}

$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestFile -Encoding utf8NoBOM

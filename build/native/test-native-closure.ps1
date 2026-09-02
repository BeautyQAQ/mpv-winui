[CmdletBinding()]
param(
    [string] $NativeDirectory = (Join-Path $PSScriptRoot '..\..\src\MpvShell.App\Assets\Native\win-x64'),

    [string] $ManifestPath = (Join-Path $PSScriptRoot '..\..\src\MpvShell.Player.LibMpv\Native\native-dependencies.lock.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$directory = [IO.Path]::GetFullPath($NativeDirectory)
$manifestFile = [IO.Path]::GetFullPath($ManifestPath)
$manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json
$handles = [Collections.Generic.List[System.IntPtr]]::new()
$handlesByFileName = [Collections.Generic.Dictionary[string, System.IntPtr]]::new(
    [StringComparer]::OrdinalIgnoreCase)

Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate uint MpvClientApiVersionDelegate();
'@

try {
    foreach ($asset in $manifest.assets | Sort-Object loadOrder, fileName) {
        $path = Join-Path $directory $asset.fileName
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "缺少原生资产：$path"
        }

        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
        if ($actualHash -ne $asset.sha256) {
            throw "$($asset.fileName) SHA-256 不匹配。预期 $($asset.sha256)，实际 $actualHash。"
        }

        $handle = [Runtime.InteropServices.NativeLibrary]::Load($path)
        $handles.Add($handle)
        $handlesByFileName.Add($asset.fileName, $handle)
        Write-Host "LOAD PASS  $($asset.fileName)"
    }

    $mpvAsset = $manifest.assets | Where-Object { $_.logicalNames -contains 'mpv' } | Select-Object -First 1
    if (-not $mpvAsset) {
        throw '清单未登记 mpv 逻辑库。'
    }

    $mpvHandle = $handlesByFileName[$mpvAsset.fileName]
    $export = [Runtime.InteropServices.NativeLibrary]::GetExport($mpvHandle, 'mpv_client_api_version')
    $versionDelegate = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer(
        $export,
        [MpvClientApiVersionDelegate])
    $version = $versionDelegate.Invoke()
    $major = $version -shr 16
    $minor = $version -band 0xffff
    if ($major -ne $manifest.expectedMpvClientApi.major -or
        $minor -lt $manifest.expectedMpvClientApi.minimumMinor) {
        throw "libmpv Client API $major.$minor 不满足清单要求。"
    }

    Write-Host "MPV API PASS  $major.$minor"
}
finally {
    for ($index = $handles.Count - 1; $index -ge 0; $index--) {
        [Runtime.InteropServices.NativeLibrary]::Free($handles[$index])
    }
}

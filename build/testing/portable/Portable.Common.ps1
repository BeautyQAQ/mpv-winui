$ErrorActionPreference = 'Stop'

function Get-TestPackageKey {
    param([Parameter(Mandatory = $true)][string]$PackageRoot)

    $identity = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\').ToUpperInvariant()
    $manifestPath = Join-Path $PackageRoot 'build-info.json'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
            if ($manifest.packageId) { $identity = [string]$manifest.packageId }
        }
        catch { Write-Warning "构建清单无法解析，将按包目录识别日志：$($_.Exception.Message)" }
    }
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity)))).Replace('-', '').Substring(0, 32).ToLowerInvariant()
    }
    finally { $hash.Dispose() }
}

function Get-TestFallbackRoot {
    param([Parameter(Mandatory = $true)][string]$PackageKey)
    $localRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localRoot)) { throw '无法定位当前用户的 LocalAppData 目录。' }
    return Join-Path (Join-Path $localRoot 'MpvShell\test-runs') $PackageKey
}

function Test-TestDirectoryWritable {
    param([Parameter(Mandatory = $true)][string]$Path)
    $probePath = Join-Path $Path ('.write-probe-' + [Guid]::NewGuid().ToString('N'))
    try {
        [void][IO.Directory]::CreateDirectory($Path)
        [IO.File]::WriteAllText($probePath, 'test', [Text.UTF8Encoding]::new($false))
        [IO.File]::Delete($probePath)
        return $true
    }
    catch { return $false }
}

function Write-TestJson {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}

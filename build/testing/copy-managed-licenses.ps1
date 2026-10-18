#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assetsPath = Join-Path $repository 'src/MpvShell.App/obj/project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
$fallbacks = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'licenses/origins.json') -Raw | ConvertFrom-Json
$packages = @($assets.libraries.GetEnumerator() | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object {
    @{ id = $_.Key; path = $_.Value.path; sha512 = $_.Value.sha512 }
})
$runtime = Get-Content -LiteralPath (Join-Path $PackageDirectory 'MpvShell.App.runtimeconfig.json') -Raw | ConvertFrom-Json
$version = ($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
$packages += @{ id = "Microsoft.NETCore.App.Runtime.win-x64/$version"; path = "microsoft.netcore.app.runtime.win-x64/$version"; sha512 = $null }
$records = @()
foreach ($package in $packages | Sort-Object { $_.id }) {
    $roots = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $package.path } | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
    if ($roots.Count -eq 0) { throw "找不到依赖包：$($package.id)" }
    $root = $roots[0]
    $nuspec = @(Get-ChildItem -LiteralPath $root -File -Filter '*.nuspec')
    if ($nuspec.Count -ne 1) { throw "依赖包元数据不唯一：$($package.id)" }
    [xml]$xml = Get-Content -LiteralPath $nuspec[0].FullName -Raw
    $license = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    $upstream = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='repository']")
    $files = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object { $_.Name -match '(?i)(license|notice|copying)' })
    $fileInputs = @($files | ForEach-Object { @{ source = $_.FullName; name = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } })
    $origin = $null
    if ($files.Count -eq 0) {
        $fallback = @($fallbacks.files | Where-Object { $_.packages -contains $package.id })
        if ($fallback.Count -eq 1) {
            if (-not $upstream -or $upstream.GetAttribute('commit') -ne $fallback[0].commit) { throw "许可原文提交与依赖包不符：$($package.id)" }
            $path = Join-Path $PSScriptRoot ('licenses/' + $fallback[0].file)
            if ((Get-FileHash -LiteralPath $path).Hash -ne $fallback[0].sha256) { throw "许可原文哈希不符：$path" }
            $fileInputs += @{ source = $path; name = 'LICENSE' }
            $origin = $fallback[0].url
        }
        elseif ($package.id.StartsWith('Microsoft.Windows.SDK.BuildTools/')) {
            # 构建工具的包只带许可 URL；使用已随原生闭包锁定的 Windows SDK 许可原文。
            $fileInputs += @{ source = Join-Path $repository 'src/MpvShell.App/Assets/ThirdParty/windows-sdk/sdk_license.rtf'; name = 'sdk_license.rtf' }
            $origin = 'ThirdParty/windows-sdk/sdk_license.rtf；构建工具，不声明其全部文件随应用发布'
        }
        else { throw "依赖包没有许可原文，请补充固定来源：$($package.id)" }
    }
    $fileInputs += @{ source = $nuspec[0].FullName; name = $nuspec[0].Name }
    $output = Join-Path $PackageDirectory ('ThirdParty/NuGet/' + $package.path)
    $copied = @($fileInputs | ForEach-Object {
        $target = Join-Path $output $_.name
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        Copy-Item -LiteralPath $_.source -Destination $target
        [ordered]@{ path = [IO.Path]::GetRelativePath($PackageDirectory, $target).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $target).Hash }
    })
    $packageHash = $package.sha512
    if (-not $packageHash) { $packageHash = (Get-Content -LiteralPath (Join-Path $root ($package.path.Split('/')[0] + '.' + $version + '.nupkg.sha512')) -Raw).Trim() }
    $records += [ordered]@{
        package = $package.id; nugetSha512 = $packageHash
        license = $(if ($license) { $license.InnerText } else { '见 nuspec 中的 licenseUrl 与随包原文' })
        supplementalOrigin = $origin; files = $copied
    }
}
$report = [ordered]@{
    description = '应用还原依赖（含构建工具）及自包含 .NET 运行时的许可/通知原文；此清单不表示每个 NuGet 包的所有 DLL 都随应用发布。'
    projectAssetsSha256 = (Get-FileHash -LiteralPath $assetsPath).Hash
    packages = $records
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PackageDirectory 'ThirdParty/nuget-license-origins.json') -Encoding utf8NoBOM
Write-Host "已复制 $($records.Count) 个依赖包的许可、通知与元数据。"

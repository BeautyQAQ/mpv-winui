#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $HdrMediaDirectory,
    [Parameter(Mandatory)][string] $P104MediaDirectory,
    [switch] $NoRestore
)

# 保留历史入口、目录和包名；新的当前版本候选包使用 publish-candidate.ps1。
& (Join-Path $PSScriptRoot 'publish-candidate.ps1') `
    -HdrMediaDirectory $HdrMediaDirectory -P104MediaDirectory $P104MediaDirectory `
    -PackageLabel 'phase1' -PackageTitle 'Phase 1 发布包' `
    -ArtifactDirectory (Join-Path $PSScriptRoot '../../artifacts/phase-1') -NoRestore:$NoRestore

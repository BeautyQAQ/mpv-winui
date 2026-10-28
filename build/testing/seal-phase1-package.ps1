#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory)

# 保留历史入口；通用封包器继续接受原有 schemaVersion 2 的 Phase 1 包。
& (Join-Path $PSScriptRoot 'seal-candidate-package.ps1') -PackageDirectory $PackageDirectory

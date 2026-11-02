[CmdletBinding()]
param(
    [string]$SqlInstance = 'localhost',
    [ValidateSet('CreatorFlow_QC')]
    [string]$Database = 'CreatorFlow_QC',
    [switch]$IncludeTenantMigrations,
    [switch]$ConfirmLegacyWorkspaceAssignment,
    [bool]$TrustServerCertificate = $true
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($IncludeTenantMigrations -and -not $ConfirmLegacyWorkspaceAssignment) {
    throw 'To apply tenant migration 012, review its legacy-workspace ownership rule and pass -ConfirmLegacyWorkspaceAssignment.'
}
$create = Join-Path $PSScriptRoot 'Create-Database.ps1'
$apply = Join-Path $PSScriptRoot 'Apply-Migrations.ps1'
$verify = Join-Path $PSScriptRoot 'Verify-Database.ps1'

Write-Host '=== 1/3 Create and tag isolated QC database ==='
& $create -SqlInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate

Write-Host '=== 2/3 Apply ordered schema migrations ==='
$applyArgs = @{
    SqlInstance = $SqlInstance
    Database = $Database
    TrustServerCertificate = $TrustServerCertificate
}
if ($IncludeTenantMigrations) {
    $applyArgs.IncludeTenantMigrations = $true
    $applyArgs.ConfirmLegacyWorkspaceAssignment = $true
}
& $apply @applyArgs

Write-Host '=== 3/3 Verify database schema and migration checksums ==='
$verifyArgs = @{
    SqlInstance = $SqlInstance
    Database = $Database
    TrustServerCertificate = $TrustServerCertificate
}
if ($IncludeTenantMigrations) { $verifyArgs.RequireTenantMigrations = $true }
& $verify @verifyArgs

Write-Host "CreatorFlow database initialization completed: $Database."

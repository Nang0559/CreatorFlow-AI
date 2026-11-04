[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8180',
    [string]$EnvPath = (Join-Path (Split-Path $PSScriptRoot -Parent) '.env'),
    [string]$AdminUsername,
    [securestring]$AdminPassword,
    [string]$OwnerUsername,
    [string]$OwnerEmail,
    [securestring]$OwnerPassword
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertFrom-SecureInput([securestring]$Value) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}
function Invoke-KeycloakAdmin([string]$Method, [string]$Path, $Body = $null) {
    $params = @{ Method = $Method; Uri = "$script:Base/admin/$Path"; Headers = @{ Authorization = "Bearer $script:Token" }; ErrorAction = 'Stop' }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ConvertTo-Json -InputObject $Body -Depth 12
    }
    Invoke-RestMethod @params
}

$script:Base = $BaseUrl.TrimEnd('/')
if (-not $AdminUsername) { $AdminUsername = Read-Host 'Keycloak admin username' }
if (-not $AdminPassword) { $AdminPassword = Read-Host 'Keycloak admin password' -AsSecureString }
if (-not $OwnerUsername) { $OwnerUsername = Read-Host 'Local CreatorFlow owner username (e.g. creatorflow-owner)' }
if (-not $OwnerEmail) { $OwnerEmail = Read-Host 'Local CreatorFlow owner email' }
if (-not $OwnerPassword) { $OwnerPassword = Read-Host 'Temporary owner password' -AsSecureString }
if ([string]::IsNullOrWhiteSpace($OwnerUsername) -or [string]::IsNullOrWhiteSpace($OwnerEmail)) {
    throw 'Owner username and email are required.'
}

$adminPlain = ConvertFrom-SecureInput $AdminPassword
$tokenResponse = Invoke-RestMethod -Method Post -Uri "$script:Base/realms/master/protocol/openid-connect/token" -ContentType 'application/x-www-form-urlencoded' -Body @{
    client_id = 'admin-cli'; grant_type = 'password'; username = $AdminUsername; password = $adminPlain
} -ErrorAction Stop
$script:Token = $tokenResponse.access_token
if ([string]::IsNullOrWhiteSpace($script:Token)) { throw 'Keycloak admin token was not issued.' }

$realmUsers = @(Invoke-KeycloakAdmin GET "realms/creatorflow/users?username=$([uri]::EscapeDataString($OwnerUsername))&exact=true")
$user = $realmUsers | Where-Object { $_.username -ceq $OwnerUsername } | Select-Object -First 1
if ($null -eq $user) {
    Invoke-KeycloakAdmin POST 'realms/creatorflow/users' @{
        username = $OwnerUsername; enabled = $true; email = $OwnerEmail
        emailVerified = $false; firstName = 'CreatorFlow'; lastName = 'Local Owner'
        requiredActions = @()
    } | Out-Null
    $realmUsers = @(Invoke-KeycloakAdmin GET "realms/creatorflow/users?username=$([uri]::EscapeDataString($OwnerUsername))&exact=true")
    $user = $realmUsers | Where-Object { $_.username -ceq $OwnerUsername } | Select-Object -First 1
}
if ($null -eq $user -or [string]::IsNullOrWhiteSpace($user.id)) { throw 'Could not resolve the local owner user in the realm.' }

$ownerPlain = ConvertFrom-SecureInput $OwnerPassword
Invoke-KeycloakAdmin PUT "realms/creatorflow/users/$($user.id)/reset-password" @{
    type = 'password'; value = $ownerPlain; temporary = $true
} | Out-Null

$roleName = 'creatorflow-local-user'
try { $null = Invoke-KeycloakAdmin GET "realms/creatorflow/roles/$roleName" }
catch {
    Invoke-KeycloakAdmin POST 'realms/creatorflow/roles' @{ name = $roleName; description = 'Local development user; workspace permissions are stored in CreatorFlow tenant membership.' } | Out-Null
}
$role = Invoke-KeycloakAdmin GET "realms/creatorflow/roles/$roleName"
Invoke-KeycloakAdmin POST "realms/creatorflow/users/$($user.id)/role-mappings/realm" @($role) | Out-Null

$clients = @(Invoke-KeycloakAdmin GET 'realms/creatorflow/clients?clientId=creatorflow-web')
$client = $clients | Where-Object { $_.clientId -ceq 'creatorflow-web' } | Select-Object -First 1
if ($null -eq $client) { throw 'Imported confidential client creatorflow-web was not found.' }
$clientSecret = Invoke-KeycloakAdmin GET "realms/creatorflow/clients/$($client.id)/client-secret"
if ([string]::IsNullOrWhiteSpace($clientSecret.value)) { throw 'Keycloak did not return the generated web client secret.' }

if (-not (Test-Path -LiteralPath $EnvPath -PathType Leaf)) { throw ".env file not found: $EnvPath" }
$envText = Get-Content -LiteralPath $EnvPath -Raw
$escapedSecret = $clientSecret.value.Replace('\','\\').Replace('"','\"')
$quotedSecret = [string][char]34 + $escapedSecret + [string][char]34
if ($envText -match '(?m)^Authentication__Oidc__ClientSecret=.*$') {
    $envText = [regex]::Replace($envText, '(?m)^Authentication__Oidc__ClientSecret=.*$', ('Authentication__Oidc__ClientSecret=' + $quotedSecret))
} else {
    $envText = $envText.TrimEnd() + [Environment]::NewLine + ('Authentication__Oidc__ClientSecret=' + $quotedSecret) + [Environment]::NewLine
}
Set-Content -LiteralPath $EnvPath -Value $envText -Encoding UTF8

Write-Host "Created/updated local Keycloak user '$OwnerUsername' and assigned realm role '$roleName'."
Write-Host "Stored the generated creatorflow-web client secret in the ignored .env file."
Write-Host 'The temporary password must be changed at first login.'
Write-Host 'IMPORTANT: the Keycloak realm role is informational. CreatorFlow workspace role is created separately through /api/v1/identity/owner/bootstrap using Authentication__OwnerBootstrapToken.'

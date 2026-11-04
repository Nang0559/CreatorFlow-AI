[CmdletBinding()]
param(
    [switch]$CheckLive,
    [string]$BaseUrl = 'http://creatorflow-id.test:8180'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$realmPath = Join-Path $PSScriptRoot '../docker/keycloak/realm-import/creatorflow-realm.json'
if (-not (Test-Path -LiteralPath $realmPath -PathType Leaf)) {
    throw "Keycloak realm import file is missing: $realmPath"
}
$realm = Get-Content -LiteralPath $realmPath -Raw | ConvertFrom-Json
if ($realm.realm -cne 'creatorflow' -or -not $realm.enabled) {
    throw 'Realm contract must define the enabled creatorflow realm.'
}

$client = @($realm.clients | Where-Object { $_.clientId -ceq 'creatorflow-web' })
if ($client.Count -ne 1) { throw 'Exactly one creatorflow-web client is required.' }
$client = $client[0]
if ($client.publicClient -ne $false -or $client.standardFlowEnabled -ne $true -or
    $client.implicitFlowEnabled -ne $false -or $client.directAccessGrantsEnabled -ne $false) {
    throw 'creatorflow-web must be confidential Authorization Code only; implicit and direct grants must be disabled.'
}
if ($client.attributes.'pkce.code.challenge.method' -cne 'S256') {
    throw 'creatorflow-web must require PKCE S256.'
}
if ($client.redirectUris -notcontains 'http://localhost:8080/signin-oidc' -or
    $client.attributes.'post.logout.redirect.uris' -notmatch 'http://localhost:8080/signout-callback-oidc' -or
    $client.webOrigins -notcontains 'http://localhost:8080') {
    throw 'Local Web redirect, post-logout redirect or origin does not match the Web OIDC configuration.'
}
if ($client.defaultClientScopes -notcontains 'profile' -or $client.defaultClientScopes -notcontains 'email' -or
    $client.optionalClientScopes -notcontains 'creatorflow-api') {
    throw 'creatorflow-web must assign profile/email and optional creatorflow-api scopes.'
}

$scope = @($realm.clientScopes | Where-Object { $_.name -ceq 'creatorflow-api' })
if ($scope.Count -ne 1) { throw 'The creatorflow-api client scope is missing or duplicated.' }
$mapper = @($scope[0].protocolMappers | Where-Object {
    $_.protocolMapper -ceq 'oidc-audience-mapper' -and $_.config.'included.custom.audience' -ceq 'creatorflow-api'
})
if ($mapper.Count -ne 1 -or $mapper[0].config.'access.token.claim' -cne 'true' -or
    $mapper[0].config.'id.token.claim' -cne 'false') {
    throw 'The creatorflow-api scope must add only the creatorflow-api audience to access tokens.'
}

Write-Host 'PASS static Keycloak realm contract: creatorflow, confidential Authorization Code + PKCE S256, scopes and API audience.'

if ($CheckLive) {
    $base = $BaseUrl.TrimEnd('/')
    $expectedIssuer = "$base/realms/creatorflow"
    $metadata = Invoke-RestMethod -Uri "$expectedIssuer/.well-known/openid-configuration" -TimeoutSec 10
    if ($metadata.issuer.TrimEnd('/') -cne $expectedIssuer) {
        throw "OIDC issuer mismatch. Expected '$expectedIssuer', received '$($metadata.issuer)'."
    }
    foreach ($field in @('authorization_endpoint','token_endpoint','jwks_uri')) {
        if ([string]::IsNullOrWhiteSpace([string]$metadata.$field)) {
            throw "OIDC discovery metadata is missing '$field'."
        }
    }
    Write-Host "PASS live OIDC discovery: $expectedIssuer"
}

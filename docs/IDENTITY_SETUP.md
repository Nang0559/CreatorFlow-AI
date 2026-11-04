# CreatorFlow AI — OIDC identity and workspace setup

This branch uses an external OpenID Connect provider for authentication. CreatorFlow does not store user passwords. The provider owns account creation, password policy, MFA, recovery and identity proofing; CreatorFlow stores only the immutable `issuer + subject` identity mapping and workspace memberships.

## Local Z240 setup — Keycloak (Development only)

The development Compose file includes Keycloak `26.8.1`, persisted in the `keycloak_data` Docker volume and bound only to the Z240 loopback interface at port `8180`. It uses Keycloak's `start-dev` mode and its development database; **this is not a production deployment**.

The same issuer hostname must resolve to the local Keycloak service in three places: Windows-hosted API and browser resolve it to loopback, while Docker Web resolves it through the Compose network alias. This setup uses `creatorflow-id.test`:
1. Open **PowerShell as Administrator** on Z240 and add the local host mapping once:
   ```powershell
   $hosts = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
   if (-not (Select-String -Path $hosts -Pattern '^\s*127\.0\.0\.1\s+creatorflow-id\.test(\s|$)' -Quiet)) {
       Add-Content -Path $hosts -Value "`r`n127.0.0.1 creatorflow-id.test"
   }
   ```
2. In the project `.env` (never commit it), set unique strong values for `KEYCLOAK_ADMIN_PASSWORD` and `Authentication__OwnerBootstrapToken`; obtain `Authentication__Oidc__ClientSecret` from Keycloak after creating the confidential client. To generate a local high-entropy hex secret without displaying an existing secret, run this in PowerShell and copy its output directly into the local `.env`:
   ```powershell
   $bytes = [byte[]]::new(32)
   [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
   [Convert]::ToHexString($bytes)
   ```
   Run it separately for the admin password and Owner bootstrap token. Keep these values out of chat, logs and Git.
3. Set `KEYCLOAK_PUBLIC_URL=http://creatorflow-id.test:8180` and the exact matching issuer `Authentication__Oidc__Authority=http://creatorflow-id.test:8180/realms/creatorflow`.
4. Start the identity provider:
   ```powershell
   docker compose --env-file .env -f compose.dev.yaml up -d keycloak
   Invoke-WebRequest http://localhost:9100/health/ready
   .\scripts\Test-KeycloakRealmContract.ps1 -CheckLive
   ```
   The management health endpoint is published only on Z240 loopback port `9100`. Open the Admin Console at `http://creatorflow-id.test:8180/admin/`. Compose mounts `docker/keycloak/realm-import/creatorflow-realm.json` and starts Keycloak with `--import-realm`, so a clean Keycloak data volume imports the `creatorflow` realm, confidential `creatorflow-web` client, PKCE S256 setting and `creatorflow-api` audience scope.
5. Run `./scripts/Initialize-KeycloakLocal.ps1`. It prompts securely for the Keycloak admin password and a temporary local owner password, creates or updates the example user (for example `creatorflow-owner`), assigns the informational `creatorflow-local-user` realm role, retrieves the generated confidential-client secret and writes it into the ignored `.env` file. The temporary password must be changed at first login. Never place real passwords in command-line arguments or Git.
6. Verify the imported client has Standard flow enabled, Direct access grants and Implicit flow disabled, redirect URI `http://localhost:8080/signin-oidc`, post-logout redirect URI `http://localhost:8080/signout-callback-oidc`, Web origin `http://localhost:8080`, and PKCE method **S256**. Confirm `creatorflow-api` is an optional client scope with an Audience mapper that adds audience `creatorflow-api` to access tokens only. Set `Authentication__Oidc__ApiScope=creatorflow-api` and `Authentication__Oidc__Audience=creatorflow-api`.
7. Sign in as the local owner and complete the one-time CreatorFlow Owner bootstrap using `Authentication__OwnerBootstrapToken` and `POST /api/v1/identity/owner/bootstrap`. The Keycloak realm role is informational only: actual Owner/Admin/Editor/Analyst/Publisher authorization is stored in CreatorFlow tenant membership. Configure OTP/MFA for Owner/Admin before any public or shared deployment.

**Existing Keycloak data volume:** realm import is non-destructive and does not overwrite an already-existing `creatorflow` realm. If you configured the realm manually before this export was added, keep the existing volume and compare its settings with the checked-in realm contract; do not run `docker compose down -v` to force an import.
9. Rebuild/recreate Web after adding the OIDC settings, and restart the native API after loading the same `.env` values. The Compose network alias `creatorflow-id.test` routes Web to Keycloak internally; the Windows hosts entry routes the browser and native API to the loopback-published port. The API must use the same `Authentication__Oidc__Authority` and `Authentication__Oidc__Audience` as Web/Keycloak.

Development-only HTTP OIDC metadata is enabled only when ASP.NET Core runs in the `Development` environment. Both Web and API continue to require HTTPS metadata outside Development. Do not carry the HTTP authority, `start-dev`, or the development database into Production. Production requires a stable HTTPS issuer, TLS, a production database (for example PostgreSQL), protected secrets, and a non-public administration surface. See the [Keycloak production guide](https://www.keycloak.org/server/configuration-production).


## 1. OIDC application registration

Register a confidential server-side web application with Authorization Code Flow and PKCE. For Production configure:
- Sign-in redirect URI: `https://<web-host>/signin-oidc`
- Sign-out callback URI: `https://<web-host>/signout-callback-oidc`
For the local Z240 Development setup above, use the exact HTTP localhost callback URIs configured on the `creatorflow-web` client.
- Scopes: `openid`, `profile`, `email`, and the API scope configured below.
- The API must issue access tokens whose audience matches `Authentication:Oidc:Audience`.
- The ID token / UserInfo response must include `iss`, `sub`, `email`, and `email_verified` for invitation acceptance.
- Enforce MFA for Owner and Admin users in the identity provider. Do not rely on an unverified email claim.

Store secrets in the deployment secret manager, not appsettings or source control.

## 2. Required runtime configuration

Web:
- `Authentication:Oidc:Authority`
- `Authentication:Oidc:ClientId`
- `Authentication:Oidc:ClientSecret`
- `Authentication:Oidc:ApiScope` (scope that yields an API access token accepted by CreatorFlow.Api)
- `Authentication:Oidc:Audience` (API-side expected audience; for this Keycloak setup: `creatorflow-api`)
- `CreatorFlowApi:BaseUrl`

API:
- `Authentication:Oidc:Authority` (must match the token issuer exactly)
- `Authentication:Oidc:Audience` (must match the access-token audience)
- `Authentication:OwnerBootstrapToken` (high-entropy one-time deployment secret)
- `ConnectionStrings:CreatorFlow`

The Web app refuses to start in Production without all four OIDC settings. The API's development API-key bootstrap is not an interactive user identity and must remain disabled outside Development.

## 3. Apply the membership lifecycle migration

Run the repository database initializer / migration runner so that `013_tenant_membership_lifecycle.sql` is applied after migrations 001–012. Verify `dbo.TenantInvitations` and `dbo.TenantSecurityAudit` exist before enabling account administration.

## 4. Bootstrap the first Owner

1. Configure `Authentication:OwnerBootstrapToken` as a secret in the API environment.
2. Sign in to the Web app using the OIDC provider.
3. Obtain an API access token for the CreatorFlow API and call `POST /api/v1/identity/owner/bootstrap` with:
   - `Authorization: Bearer <access-token>`
   - `X-CreatorFlow-Owner-Bootstrap: <one-time-secret>`
   - JSON body: `{ "name": "My workspace", "slug": "my-workspace" }`
4. Bootstrap is globally one-time: it fails if any active Owner already exists. The resulting identity is the workspace Owner. Use the existing `legacy-workspace` slug only when intentionally assigning the first Owner to that migrated workspace.

Do not insert membership rows manually and do not use email as the identity key.

## 5. Workspace selection and membership

- `GET /api/v1/identity/workspaces` lists only active memberships for the signed-in OIDC identity.
- `POST /auth/select-workspace/{tenantId}` verifies the selected workspace against that list and stores the selection in the protected server cookie.
- Web-to-API requests forward the server-held access token and selected tenant header. The API independently resolves the active membership; the header is only a selector, never authorization.

## 6. Use the account and workspace screen

After configuring OIDC and completing the initial Owner bootstrap, open `/account` in CreatorFlow. The screen lists the signed-in identity's workspaces, lets the user switch workspace, and provides Owner/Admin member invitation controls and Owner role/suspension controls. Workspace selection and logout use the Web app's CSRF-protected endpoints. A user without a selected workspace cannot access tenant-protected business APIs.

## 7. Invite and manage members

- Owner/Admin: `POST /api/v1/identity/workspaces/{tenantId}/invitations` with `{ "email": "...", "role": "Editor" }`.
- The API returns a random invitation token once. Only its SHA-256 hash is stored. Deliver the raw token to the invitee through a trusted channel; SMTP delivery is not configured by this change.
- Invitee signs in / creates an account at the configured identity provider, verifies the invited email, then calls `POST /api/v1/identity/invitations/accept` with `{ "token": "..." }`.
- Owner/Admin can list members and pending invitations and revoke invitations. Only Owner can change roles or suspend memberships. Owner cannot be granted through an invitation; the workspace must always retain at least one active Owner.

Roles: Owner, Admin, Editor, Analyst, Publisher. The central mutation matrix is deny-by-default: Owner/Admin may mutate tenant workflows (with endpoint-specific Owner/Admin checks still applied); Editor may mutate content, topics, channel/video intake, Share Inbox, trend feedback, Style Lab/profile, AI content-generation, video render and discovery-search routes, but not identity administration, AI provider registry or connector credential management. Editor cannot approve/reject learning proposals; those profile-changing review actions are Owner/Admin-only until a dedicated Reviewer role exists. Analyst is read-only; Publisher may mutate only `/api/v1/publishing`. No publishing endpoint exists yet, so Publisher currently has no write operation. Sensitive GET routes for identity administration, AI provider registry, connector credentials and billing are Owner/Admin-only; ordinary operational reads remain available according to endpoint policy. Resource-level ownership and cross-tenant negative tests remain release gates.

## 8. Session and account protection

- Web uses a host-only, Secure, HttpOnly cookie with an eight-hour lifetime and sliding renewal.
- OIDC uses Authorization Code + PKCE; tokens are held in the protected server-side authentication ticket, not browser JavaScript.
- API access-token refresh/rotation is not implemented yet. When the provider-issued API access token expires, API calls require reauthentication; do not request `offline_access` until a secure refresh-token rotation and server-side ticket update flow is implemented.
- `GET /auth/csrf` issues the anti-forgery token required by workspace selection and logout POSTs.
- `POST /auth/logout` clears the local cookie and initiates OIDC provider sign-out.
- `GET /auth/session` returns JSON with authentication state, subject, selected workspace and access-token expiry (never the token). For an unauthenticated request it returns `authenticated: false` rather than redirecting to the identity provider.
- MFA, password reset, account lockout, compromised-credential response and provider-side session revocation must be configured at the OIDC provider. The current implementation does not provide a local password database, central per-device session-revocation console, or forced sign-out of every active CreatorFlow cookie session.

## 9. Tenant isolation guarantees and remaining gates

All API endpoints are protected by a fallback policy unless explicitly public or using the OIDC-only onboarding policy. Existing EF Core tenant query filters and write stamping remain in force. API tenant context is resolved from the validated `iss + sub` membership and selected tenant; caller-provided TenantId never grants access. Analyst and Publisher mutation restrictions are applied centrally.

Before public SaaS launch, complete cross-tenant negative tests for every endpoint and storage adapter, fine-grained per-operation role policies, background-worker tenant context, server-side session revocation, email delivery, MFA policy verification, and live identity-provider contract tests.

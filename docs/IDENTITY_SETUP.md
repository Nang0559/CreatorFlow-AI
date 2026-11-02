# CreatorFlow AI — OIDC identity and workspace setup

This branch uses an external OpenID Connect provider for authentication. CreatorFlow does not store user passwords. The provider owns account creation, password policy, MFA, recovery and identity proofing; CreatorFlow stores only the immutable `issuer + subject` identity mapping and workspace memberships.

## 1. OIDC application registration

Register a confidential server-side web application with Authorization Code Flow and PKCE. Configure:
- Sign-in redirect URI: `https://<web-host>/signin-oidc`
- Sign-out callback URI: `https://<web-host>/signout-callback-oidc`
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

## 6. Invite and manage members

- Owner/Admin: `POST /api/v1/identity/workspaces/{tenantId}/invitations` with `{ "email": "...", "role": "Editor" }`.
- The API returns a random invitation token once. Only its SHA-256 hash is stored. Deliver the raw token to the invitee through a trusted channel; SMTP delivery is not configured by this change.
- Invitee signs in / creates an account at the configured identity provider, verifies the invited email, then calls `POST /api/v1/identity/invitations/accept` with `{ "token": "..." }`.
- Owner/Admin can list members and pending invitations and revoke invitations. Only Owner can change roles or suspend memberships. Owner cannot be granted through an invitation; the workspace must always retain at least one active Owner.

Roles: Owner, Admin, Editor, Analyst, Publisher. Analyst is read-only. Publisher mutations are denied except future publishing endpoints; Editor/Admin/Owner can use current content workflows. Fine-grained per-feature authorization remains a release gate.

## 7. Session and account protection

- Web uses a host-only, Secure, HttpOnly cookie with an eight-hour lifetime and sliding renewal.
- OIDC uses Authorization Code + PKCE; tokens are held in the protected server-side authentication ticket, not browser JavaScript.
- `GET /auth/csrf` issues the anti-forgery token required by workspace selection and logout POSTs.
- `POST /auth/logout` clears the local cookie and initiates OIDC provider sign-out.
- `GET /auth/session` reports the authenticated subject, selected workspace and access-token expiry (never the token).
- MFA, password reset, account lockout, compromised-credential response and provider-side session revocation must be configured at the OIDC provider. The current implementation does not provide a local password database or a central per-device session-revocation console.

## 8. Tenant isolation guarantees and remaining gates

All API endpoints are protected by a fallback policy unless explicitly public or using the OIDC-only onboarding policy. Existing EF Core tenant query filters and write stamping remain in force. API tenant context is resolved from the validated `iss + sub` membership and selected tenant; caller-provided TenantId never grants access. Analyst and Publisher mutation restrictions are applied centrally.

Before public SaaS launch, complete cross-tenant negative tests for every endpoint and storage adapter, fine-grained per-operation role policies, background-worker tenant context, server-side session revocation, email delivery, MFA policy verification, and live identity-provider contract tests.

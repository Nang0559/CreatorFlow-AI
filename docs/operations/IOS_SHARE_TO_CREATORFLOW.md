# Receive a public TikTok share on iPhone

1. Create an iOS Shortcut named **CreatorFlow – Nhận video**.
2. Enable **Show in Share Sheet** and accept URLs.
3. Add: Get URLs from Shortcut Input → Get Item from List (First) → URL Encode.
4. Open this URL using the encoded variable: `https://CreatorFlow.fcc-vn.com.vn/trends?sharedUrl=` + encoded URL.

The public Web origin for this workspace is `https://CreatorFlow.fcc-vn.com.vn`. Configure the HTTPS reverse proxy/tunnel to forward this hostname to the Web container at `http://127.0.0.1:8080`. Do not forward public traffic directly to API, AI or SQL ports.
5. The existing Trends page reads `sharedUrl` and saves it to the tenant Share Inbox after sign-in.

The Z240 worker only attempts a public TikTok page and a directly exposed media URL on approved HTTPS host suffixes. It does not sign in, evade anti-bot controls, solve CAPTCHAs, access private videos, or bypass DRM. Redirects outside the allowlist are rejected. Public availability is not a reuse license: three jobs are created as `AwaitingRightsReview` and are not rendered until the workspace owner explicitly confirms rights in the Share Inbox.

Only expose the Web container through the HTTPS tunnel/reverse proxy. Keep API 7001, AI 8001 and SQL 1433 private. The iOS Shortcut is a share-sheet action; a website cannot install itself as a native iOS share extension.

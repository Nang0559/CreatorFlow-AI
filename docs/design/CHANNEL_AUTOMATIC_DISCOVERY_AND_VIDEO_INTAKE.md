# Automatic channel discovery and permitted video intake

## Product behavior

Following a channel means CreatorFlow should periodically look for new public/officially exposed metadata without requiring the creator to share each video URL. A shared URL is an optional priority signal, not the source of truth and not a prerequisite for discovery.

The intended flow is:

1. A tenant follows a channel and enables its scan schedule.
2. A registered, permitted connector fetches channel/video metadata only within its documented access scope.
3. The scan run records provider, cursor, time window, coverage and errors.
4. New candidates are upserted by tenant + platform + external video ID. Repeated scans must not create duplicate candidates.
5. Candidate ranking uses topic profile, Vietnam market fit, available metrics and optional share-link priority.
6. Download is a separate decision. Metadata visibility does not imply media-download rights.
7. A download job remains `AwaitingRightsReview` until an explicit, auditable rights basis is recorded.
8. An approved connector downloads to tenant-isolated storage, verifies size/hash/media, and links the resulting asset to the candidate.
9. Retry/backoff, cancellation, deduplication and worker lease recovery are required.

## Safety and provider policy

- No browser login, QR login, cookie/session extraction, remote phone control, fingerprint spoofing or access-control circumvention.
- Do not automate likes, follows, comments, shares or publishing.
- Never fetch arbitrary user-submitted URLs from the server. Resolve only provider-owned canonical identifiers through a registered connector to avoid SSRF.
- Connector registration requires documented provider terms, allowed endpoints, scopes, rate limits, media rights, retention/deletion rules and an owner for credential rotation.
- A public metadata feed is not permission to download the underlying video. Download requires one of: owner-provided file, a license, an official permitted media API, or verified public-domain basis.
- If no approved media connector exists, retain metadata/candidate only and show “Chưa có nguồn tải được cấp phép”; do not mark a download as completed.

## Persistence introduced by migration 021

- `DiscoveryChannelSyncStates`: tenant-scoped per-channel enablement, polling interval, next scan, last scan and bounded failure state.
- `DiscoveryVideoDownloadJobs`: tenant-scoped unique job per candidate, explicit rights review, durable status, attempt count, lease timestamp, output path and SHA-256.
- Composite foreign keys bind channel/candidate references to the same tenant.
- Queue primitives are intentionally inert until an approved connector and worker are deployed.

## Still required before automatic operation

1. Implement an `IDiscoveryConnector` contract with provider capability declarations (metadata scan, pagination/cursor, permitted media download).
2. Add at least one approved provider adapter. Douyin/TikTok support must be based on an authorized/official or otherwise documented permitted source; do not assume an undocumented endpoint is stable or allowed.
3. Add a scheduler/worker that claims due sync states using SQL locking, records scan runs, upserts candidates, applies rate limits and schedules next scans with exponential backoff.
4. Add a rights-review endpoint/UI and audit actor/time/basis before any media job becomes `Queued`.
5. Add a download worker with safe provider ID resolution, tenant storage path enforcement, size/time limits, MIME/ffprobe verification, hash dedupe, atomic file move and retry/cancel semantics.
6. Link verified files to the existing asset library and render workflow.
7. Add tests for tenant isolation, duplicate scans, cursor continuation, rate limiting, stale leases, rights gating, cancellation, partial downloads, hash collisions and connector outage.
8. Verify against the target Windows/SQL/Docker environment before enabling a schedule.

## Current honest status

Migration 021 creates durable tenant-safe sync/download queue storage. It does not provide a live Douyin/TikTok connector, scheduler, rights-review UI, or downloader. The feature is therefore not yet operational and must remain disabled until the above components and evidence exist.


## Downloader technology decision and source-file integrity

### Chosen implementation boundary

The downloader is **not FFmpeg**. FFmpeg/FFprobe are the post-acquisition media-processing and validation tools. The acquisition layer should use:

- A provider-specific, permission-checked connector to resolve a canonical provider video ID into an approved media URL or a provider-issued file stream.
- .NET `HttpClient` streaming for the actual transfer when the connector returns a documented, authorized HTTP(S) media URL/stream. Do not accept arbitrary URLs from the browser.
- A bounded temporary-file writer with cancellation, connect/read timeouts, maximum byte limits, retry/backoff, and atomic move into tenant-scoped storage.
- FFprobe to inspect container, codecs, duration, dimensions, streams and file readability after download.
- SHA-256 to identify duplicate bytes; store provider ID, canonical source reference, acquisition time, connector/version, rights basis, original filename/MIME, byte size and hash as provenance.

Do not add a generic yt-dlp fallback for arbitrary Douyin/TikTok URLs. A third-party downloader does not grant rights, platform permission or a stable/approved access method. It must not be used to bypass login, access controls, rate limits, DRM, signed URL restrictions or watermark/attribution controls. A connector may only be enabled after its exact provider/source method and permitted scope are documented and reviewed.

### Original file and watermark/identity policy

- Preserve the acquired source file byte-for-byte as an immutable **source master**. Never overwrite it with a rendered/cleaned version.
- Record SHA-256 and provenance before any transformation. Recalculate and record a separate hash for every derivative.
- Do not remove, obscure or alter a creator/platform watermark, attribution, ownership mark or other provenance identifier to disguise origin or evade platform controls.
- If the rights holder supplies a separate clean master, or an authorized source explicitly provides a no-watermark file, that file may be ingested as the source master with its own provenance and rights evidence.
- The editing pipeline may create transformative derivatives (crop, subtitles, narration, graphics, layout, audio mix) only within the documented license/permission. Preserve required attribution and do not imply ownership.
- Keep source master and derivatives in separate tenant-scoped paths and expose the distinction in the asset record/UI.

### Required acceptance tests before enabling a downloader

1. No network request is made while rights status is `AwaitingRightsReview`.
2. A download can only be claimed when a valid rights basis and reviewer/audit timestamp exist.
3. Arbitrary browser-supplied URLs and non-HTTPS URLs are rejected; connector resolves provider IDs only.
4. Cross-tenant candidate IDs cannot be claimed or written to another tenant's storage.
5. Cancel, timeout, truncated stream, oversized response and retry do not leave a file marked Completed.
6. A successful transfer is atomically committed only after size, hash and FFprobe checks pass.
7. Source master is immutable; derivative generation never changes its bytes/hash.
8. Watermark/attribution metadata is preserved and no watermark-removal step exists in the pipeline.
9. Duplicate provider IDs and duplicate hashes are handled idempotently without cross-tenant data leakage.
10. Logs contain correlation IDs and error codes but no signed media URLs, tokens or credentials.

Until these tests pass with a reviewed connector, the UI must describe automatic download as unavailable rather than implying that a queue row is a downloaded video.

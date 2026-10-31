# Architecture

## Boundaries
- Creator workspace: editorial review and creator actions.
- API: authenticated, versioned application contracts.
- Domain: content lifecycle invariants, independent of infrastructure.
- Application: use cases coordinated through ports.
- Infrastructure: SQL Server, object storage, queues, provider adapters.
- Workers: durable asynchronous jobs with idempotency and bounded retries.
- AI services: model capabilities behind provider-neutral interfaces; output is always a draft.
- Media engine: deterministic FFmpeg operations from validated job specifications.

## Workflow
Idea or permitted source -> rights/provenance check -> trend/context analysis -> script draft -> human script approval -> narration/subtitles -> render -> human final approval -> official publishing API -> analytics.

## Non-functional rules
- No unauthorized scraping, watermark removal, or platform-policy evasion.
- Do not process source assets without documented rights/permission.
- Secrets never enter source control or logs.
- Store media outside web root; validate MIME, size, duration, and file signatures.
- Enforce authorization at API and storage boundaries.
- Require explicit human approval before publishing.
- Add outbox/inbox patterns with durable queue integration.

The bootstrap does not claim queue, authentication, storage, actual model calls, or publishing are implemented.

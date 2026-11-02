# CreatorFlow-AI – Go-live readiness gates

This document prevents a green build or a healthy local URL from being mistaken for production readiness.

## Implemented in the current branch
- Responsive mobile/desktop workspace with five primary areas.
- Tenant-scoped Share Inbox with HTTPS host validation, deduplication, state transitions and API routes.
- iPhone share-sheet handoff through a user-created Apple Shortcut; pasted-link fallback remains available.
- Discovery ranking and query-plan AI baseline from the previous implementation.
- Feedback evidence aggregation primitives and schema.
- Style Lab experiment tracking, metric capture and winner selection.
- Tenant-scoped video source library with rights confirmation, file-size/extension/signature checks and SHA-256 metadata.
- Durable SQL-backed video render queue: After Effects creates the composition and PNG sequence; FFmpeg encodes H.264/AAC MP4; authenticated preview/download endpoints support byte ranges.
- The Assets UI can submit 2–3 independent style variants and displays job status, preview and MP4 download.
- Authenticated editing-package download remains available for each style variant.
- HP Z240 setup script checks tenant migration prerequisites and detects optional editors.

## Not production-complete yet – do not claim these are connected
1. **Live trend providers:** no verified, permitted Douyin/TikTok provider is wired to continuously collect channel/video/sound metrics. Manual and user-shared inputs are not equivalent to live discovery.
2. **Share-link resolution:** links are durably received and deduplicated, but the current Share Inbox does not resolve short links or automatically identify a creator/channel/video and feed the discovery ranker. Do not imply that one shared URL immediately creates a followed channel.
3. **Discovery orchestration:** topic query planning/ranking primitives exist; scheduled provider execution, candidate persistence, source health, rate limiting, retries and ranking refresh still need end-to-end implementation.
4. **Feedback learning loop:** existing feedback and aggregation primitives are not yet a fully activated SQL → LLM interpretation → reviewed rule → next-ranking pipeline. Do not claim the system has learned until that full path is proven.
5. **Media ingest and understanding:** no production media download/permission workflow, video scene understanding, speech transcription, audio/music analysis or asset lifecycle has been acceptance-tested end to end.
6. **Editing/rendering:** AE + FFmpeg rendering is implemented in code, but visual quality, Adobe template availability, fonts, codec compatibility, audio sync, long renders, retries and performance are not yet accepted on the Z240. It is not a native CapCut project and does not yet provide advanced B-roll, speech transcription, TTS, beat-synced music or a professional effects library.
7. **Publishing/account integration:** no platform publishing or account OAuth is enabled. Preserve phone-only Douyin login and read-only/no-interaction policy.
8. **Production identity and operations:** production OIDC, HTTPS, backup/restore, observability, retention, rate limits, incident handling and secret rotation must be configured and tested before public deployment.

## Required acceptance evidence before Go-live
- [ ] HP Z240 SQL migrations 001–010 and 014–018 applied with verified checksums; tenant migrations 011–013 present and all required tenant/composite FKs trusted.
- [ ] Full .NET and Python test suites pass on the exact release commit.
- [ ] Docker images build on the exact release commit; containers stay healthy after reboot.
- [ ] Web/API/AI health, readiness, API authorization and tenant isolation verified against SQL.
- [ ] iPhone Shortcut receives a Douyin share and the Share Inbox stores it once; duplicate submission is idempotent.
- [ ] Share links are not fetched server-side and unsupported URLs are rejected.
- [ ] Editing package downloads; AE JSX creates a project and renders a PNG sequence; FFmpeg exports an MP4 on the HP Z240.
- [ ] Actual exported video passes dimensions, codec, duration, audio sync, caption safe-area, playback and human visual review for all 2–3 selected styles.
- [ ] Render worker retry/timeout/restart behavior is verified, and failed/abandoned jobs are recoverable without duplicate output.
- [ ] Live data provider is contract/policy-reviewed, source attribution and refresh limits are recorded, and a real discovery run produces persisted candidates.
- [ ] Feedback is durably stored, interpreted, reviewed, activated in scoped ranking rules and demonstrably changes later ranking; evaluation uses held-out/time-split evidence.
- [ ] Backup restore and rollback are rehearsed; monitoring and incident response are available.
- [ ] Security review confirms no Douyin credentials/cookies/session are collected and no platform interactions occur.

## Release policy
A build passing CI is necessary but not sufficient. Until every applicable gate has evidence on the target environment, label the deployment **Local Pilot / Pre-Go-live**, not Production.

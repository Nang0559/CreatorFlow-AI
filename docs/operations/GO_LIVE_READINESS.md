# CreatorFlow-AI – Go-live readiness gates

This document prevents a green build or a healthy local URL from being mistaken for production readiness.

## Implemented in the current branch
- Responsive mobile/desktop workspace with five primary areas.
- Tenant-scoped Share Inbox with HTTPS host validation, deduplication, state transitions and API routes.
- iPhone share-sheet handoff through a user-created Apple Shortcut; pasted-link fallback remains available.
- Discovery ranking and query-plan AI baseline from the previous implementation.
- Feedback evidence aggregation primitives and schema.
- Style Lab experiment tracking, metric capture and winner selection.
- Authenticated editing-package download for each style variant:
  - edit-plan JSON, script, shot-list CSV, CapCut instructions and an editable After Effects JSX starter.
- HP Z240 setup script now checks tenant migration prerequisites and detects optional editors.

## Not production-complete yet – do not claim these are connected
1. **Live trend providers:** no verified, permitted Douyin/TikTok provider is wired to continuously collect channel/video/sound metrics. Manual and user-shared inputs are not equivalent to live discovery.
2. **Share-link resolution:** links are durably received and deduplicated, but the current Share Inbox does not resolve short links or automatically identify a creator/channel/video and feed the discovery ranker. Do not imply that one shared URL immediately creates a followed channel.
3. **Discovery orchestration:** topic query planning/ranking primitives exist; scheduled provider execution, candidate persistence, source health, rate limiting, retries and ranking refresh still need end-to-end implementation.
4. **Feedback learning loop:** existing feedback and aggregation primitives are not yet a fully activated SQL → LLM interpretation → reviewed rule → next-ranking pipeline. Do not claim the system has learned until that full path is proven.
5. **Media ingest and understanding:** no production media download/permission workflow, video scene understanding, speech transcription, audio/music analysis or asset lifecycle has been acceptance-tested end to end.
6. **Editing/rendering:** current ZIP is a handoff package and AE starter, not a native CapCut project or finished video. CapCut effects are applied in CapCut manually. AE template rendering and export are not yet automated/validated on the Z240.
7. **Publishing/account integration:** no platform publishing or account OAuth is enabled. Preserve phone-only Douyin login and read-only/no-interaction policy.
8. **Production identity and operations:** production OIDC, HTTPS, backup/restore, observability, retention, rate limits, incident handling and secret rotation must be configured and tested before public deployment.

## Required acceptance evidence before Go-live
- [ ] HP Z240 SQL migrations 001–010 and 014–016 applied with verified checksums; tenant migrations 011–013 present and tenant FKs trusted.
- [ ] Full .NET and Python test suites pass on the exact release commit.
- [ ] Docker images build on the exact release commit; containers stay healthy after reboot.
- [ ] Web/API/AI health, readiness, API authorization and tenant isolation verified against SQL.
- [ ] iPhone Shortcut receives a Douyin share and the Share Inbox stores it once; duplicate submission is idempotent.
- [ ] Share links are not fetched server-side and unsupported URLs are rejected.
- [ ] Editing package downloads; AE JSX creates a project; CapCut manual workflow is tested with the installed editor.
- [ ] Actual exported video passes dimensions, codec, duration, audio sync, caption safe-area, playback and visual review.
- [ ] Live data provider is contract/policy-reviewed, source attribution and refresh limits are recorded, and a real discovery run produces persisted candidates.
- [ ] Feedback is durably stored, interpreted, reviewed, activated in scoped ranking rules and demonstrably changes later ranking; evaluation uses held-out/time-split evidence.
- [ ] Backup restore and rollback are rehearsed; monitoring and incident response are available.
- [ ] Security review confirms no Douyin credentials/cookies/session are collected and no platform interactions occur.

## Release policy
A build passing CI is necessary but not sufficient. Until every applicable gate has evidence on the target environment, label the deployment **Local Pilot / Pre-Go-live**, not Production.

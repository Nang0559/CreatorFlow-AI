# CreatorFlow design documentation

This directory is the normative as-built design for the implemented CreatorFlow foundation. It describes what the repository currently contains, how components fit together, invariants enforced by code, and remaining boundaries.

## Reading order
1. [System overview](SYSTEM_OVERVIEW.md) — platform boundaries, deployment shape and end-to-end workflow.
2. [Data model](DATA_MODEL.md) — implemented SQL entities, relationships and constraints.
3. [Style Lab and Style Learning](STYLE_LAB_AND_LEARNING.md) — experiment lifecycle, measurement, evidence, analysis and creator review.
4. [As-built status and gaps](AS_BUILT_STATUS.md) — implemented capabilities versus outstanding work.

## Authority and change control
- Source code and SQL scripts are the implementation authority.
- These documents describe current behavior, not intended future capability.
- API paths and payloads are documented in ../API.md; product workflow in ../CONTENT_WORKFLOW.md; delivery sequencing in ../ROADMAP.md.
- When behavior changes, update design docs, API contract, SQL notes and tests in the same change.
- A table, UI field, endpoint or provider interface alone does not prove an external integration is complete.

## Scope
The as-built design covers content projects, topic and Douyin discovery metadata, Vietnam trend observations, controlled 2–3-variant Style Lab experiments, creator style profiles/evidence, and deterministic topic-aware style learning.

It does not claim Douyin/TikTok connectors, automatic trend/music ingestion, media downloading/rendering, product-cart integration, TikTok publishing, analytics synchronization, multi-tenant identity or durable background workers are implemented.

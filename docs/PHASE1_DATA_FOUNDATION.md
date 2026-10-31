# Phase 1 — Data foundation

## Implemented
- Content project aggregate validates required title and 200-character limit.
- Rename and archive operations enforce lifecycle restrictions.
- Application handlers create and page through projects.
- EF Core SQL Server repository persists projects and reads without tracking.
- API exposes paged listing and project creation when SQL Server is configured.
- Missing database configuration returns HTTP 503 instead of fake empty data.
- SQL Server DDL is idempotent for initial table creation.

## Security boundary
The project write endpoint is not production-ready until authentication, authorization, tenant scoping, and audit attribution are implemented. Do not expose this API publicly yet. The next increment must add identity and tenant ownership before enabling creator-facing writes.

## Apply schema
Run database/schema/002_content_projects.sql only against a dedicated CreatorFlow database. Do not point it at an unrelated or production database. The application expects the CreatorFlow connection string.

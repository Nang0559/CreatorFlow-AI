# Phase 1 — Data foundation

## Implemented
- Content project aggregate validates required title and 200-character limit.
- Rename and archive operations enforce lifecycle restrictions.
- Application handlers create and page through projects.
- EF Core SQL Server repository persists projects and reads without tracking.
- API exposes paged listing and project creation when SQL Server is configured.
- Project endpoints require an API key supplied in the X-CreatorFlow-Api-Key header.
- API key must be at least 32 characters and is compared using a constant-time comparison.
- Missing database configuration returns HTTP 503 rather than fake empty data.
- SQL Server DDL is idempotent for initial table creation.

## Security boundary
The API key is a bootstrap service-to-service gate, not a complete multi-user identity system. Before public creator access, add user accounts, role/tenant authorization, per-user audit attribution, key rotation, rate limiting, and secret-manager integration. Never commit the key. Health and root metadata endpoints intentionally remain public.

## Apply schema
Run database/schema/002_content_projects.sql only against a dedicated CreatorFlow database. Do not point it at an unrelated or production database. The application expects the CreatorFlow connection string.

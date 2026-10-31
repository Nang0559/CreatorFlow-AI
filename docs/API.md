# CreatorFlow API

All `/api/v1/*` endpoints require the `X-CreatorFlow-Api-Key` header. Configure `Authentication__ApiKey` with at least 32 characters and `ConnectionStrings__CreatorFlow` for SQL Server. `/health` and `/` are public health/metadata endpoints.

## Content projects

- `GET /api/v1/content-projects?skip=0&take=20`
- `POST /api/v1/content-projects` with `{ "title": "..." }`

## Topic discovery

- `GET /api/v1/topics?skip=0&take=50`
- `POST /api/v1/topics` with `{ "name": "Home gadgets", "keywords": "kitchen, home organization" }`
- `PUT /api/v1/topics/{id}` with `{ "name": "...", "keywords": "..." }`
- `POST /api/v1/topics/{id}/deactivate`

## Douyin source channels

- `GET /api/v1/douyin/channels?followed=true`
- `POST /api/v1/douyin/channels` with `{ "douyinSecUserId": "...", "displayName": "...", "profileUrl": "https://www.douyin.com/user/..." }`
- `POST /api/v1/douyin/channels/{id}/follow`
- `POST /api/v1/douyin/channels/{id}/unfollow`

## Douyin video candidates

- `GET /api/v1/douyin/videos?status=Candidate&discoveryMethod=TopicExplore`
- `POST /api/v1/douyin/videos` with Douyin video ID, HTTPS source URL, optional `sourceChannelId`, optional `contentTopicId` and `discoveryMethod` (`FollowedChannel`, `TopicExplore`, `Manual`).
- `POST /api/v1/douyin/videos/{id}/select`
- `POST /api/v1/douyin/videos/{id}/reject`
- `POST /api/v1/douyin/videos/{id}/queue`

List endpoints support `skip` and `take` (1..100). Duplicate Douyin channel/video identifiers return HTTP 409. Invalid state transitions return HTTP 409.

## Important integration boundary

These endpoints persist creator-entered or connector-supplied metadata. They do not themselves browse Douyin, authenticate to Douyin, follow accounts on Douyin, fetch a creator feed, or download video files. A future connector must be separately implemented and validated; the current API must not imply those operations have happened.

## Vietnam market trend intelligence

Trend kinds: `CreatorChannel`, `Video`, `Sound`. Platforms: `TikTokVietnam`, `YouTubeShortsVietnam`, `FacebookReelsVietnam`, `Other`.

- `GET /api/v1/vietnam-trends?kind=Sound&platform=TikTokVietnam`
- `POST /api/v1/vietnam-trends` with kind, platform, externalId, sourceUrl (HTTPS), creator/title/topic, optional soundName/soundArtist, publishedAt and the observed views/likes/comments/shares/saves.
- `POST /api/v1/vietnam-trends/{id}/observe` with a newer metrics snapshot.

The API currently stores the latest observed metrics on each signal. The schema/API do not imply automatic access to platform trend feeds or music charts; records must be entered manually or supplied by a separately validated connector.

## Style Lab experiments

Create an experiment linked optionally to a content project with exactly 2 or 3 variants:

- `POST /api/v1/style-experiments` with `name`, a testable `hypothesis`, optional `contentProjectId`, and `variants[]`. Each variant needs `name`, `hook`, `speakingStyle`, `editPacing`, `audioApproach` and `script`.
- `GET /api/v1/style-experiments?status=Running`
- `POST /api/v1/style-experiments/{id}/variants/{variantId}/ready`
- `POST /api/v1/style-experiments/{id}/start`
- `POST /api/v1/style-experiments/{id}/variants/{variantId}/publish`
- `POST /api/v1/style-experiments/{id}/variants/{variantId}/measure` with views, likes, comments, shares, saves, followsGained, productClicks, orders, averageWatchSeconds and videoDurationSeconds.
- `POST /api/v1/style-experiments/{id}/evaluate` with `winningVariantId`.

An experiment cannot start until every variant is ready, and cannot be evaluated until every variant has been published and measured. Engagement rate is descriptive; the creator explicitly selects the winner after considering the stated hypothesis and the wider performance evidence.

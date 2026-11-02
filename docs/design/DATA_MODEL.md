# Data model — implemented schema

This document records the current SQL Server/EF Core model. Numbered scripts under database/schema are the DDL deployment sequence; CreatorFlowDbContext contains the EF Core mappings.

## 1. Entity map

| Entity/table | Purpose | Relationships and constraints |
|---|---|---|
| Tenants | SaaS workspace identity with unique slug and Active/Suspended lifecycle | Unique Slug |
| TenantMemberships | External OIDC issuer+subject membership and workspace role | Unique TenantId + issuer + subject; tenant FK; Owner/Admin/Editor/Analyst/Publisher |
| ContentProjects | Work container for a content idea/production | Optional reference from StyleExperiments; deleting project sets link to NULL |
| ContentTopics | Creator-defined topic and keywords | Can classify a Douyin candidate |
| DouyinSourceChannels | Douyin creator/channel metadata and follow state | Unique DouyinSecUserId; index on follow state/last checked |
| DouyinVideoCandidates | Discovered or manually submitted video metadata | Unique DouyinVideoId; optional channel/topic references |
| VietnamTrendSignals | Vietnam creator/video/sound trend identity and latest metrics | Unique platform + kind + external ID |
| VietnamTrendMetricSnapshots | Immutable trend metric observations | FK to trend signal, cascade delete; unique signal + observation time |
| StyleExperiments | Hypothesis and 2–3 variant comparison | Optional project FK, optional topic, optional winning variant |
| StyleExperimentVariants | Creative variant and latest measured summary | FK to experiment; unique experiment + variant name |
| StyleVariantMetricObservations | Append-only time-series snapshots for each variant | FK to variant, unique variant + ObservedAt; indexed for chronological reads |
| CreatorStyleProfiles | Creator positioning and editable guidance | Unique ProfileKey |
| CreatorStyleEvidence | Snapshot of confirmed winner and measured results | FK to profile, experiment and winner; unique profile + experiment |
| StyleLearningProposals | Reviewable AI/statistical learning drafts with evidence snapshot and algorithm version | FK to profile; Pending/Approved/Rejected state |
| CreatorStyleProfileVersions | Immutable approved creator-style guidance snapshots | FK to profile and optional approved proposal; unique profile + version number and proposal |
| TrendSelectionFeedback | Immutable creator Select/Reject/Watch decisions with candidate and feature snapshots | FK to candidate; indexed by candidate/time and decision/reason/time; SQL trigger blocks UPDATE/DELETE |
| TrendCandidateOutcomeObservations | Downstream adaptation, publishing and measured outcome evidence | FK to feedback; indexed by feedback/time; SQL trigger blocks UPDATE/DELETE |

## 2. Important columns and enumerations

### Discovery
- DouyinSourceChannels: DouyinSecUserId, DisplayName, ProfileUrl, IsFollowed, FollowedAt, LastCheckedAt.
- DouyinVideoCandidates: DouyinVideoId, SourceChannelId, ContentTopicId, SourceUrl, DiscoveryMethod, Status.
- Discovery method: FollowedChannel, TopicExplore, Manual.
- Candidate status: Candidate, Selected, Rejected, QueuedForAdaptation, Adapted.

### Vietnam trend intelligence
- Kind: CreatorChannel, Video, Sound.
- Platform: TikTokVietnam, YouTubeShortsVietnam, FacebookReelsVietnam, Other.
- Identity is unique by (Platform, Kind, ExternalId).
- Snapshots record views, likes, comments, shares, saves and ObservedAt. They are observations, not an inferred trend score.

### Style experiments
- Experiment status: Draft, Running, Evaluated, Archived.
- Variant status: Draft, Ready, Published, Measured.
- Experiment fields: Name, Hypothesis, nullable Topic, optional ContentProjectId and nullable WinningVariantId.
- Variant dimensions: Hook, SpeakingStyle, EditPacing, AudioApproach, Script and optional CreativeNotes.
- Metrics: views, likes, comments, shares, saves, follows gained, product clicks, orders, average watch seconds, video duration, publication and measurement timestamps.
- Engagement rate = (likes + comments + shares + saves) / views × 100. It is descriptive, not an automatic winner score.

### Creator style
- CreatorStyleProfiles: unique ProfileKey, DisplayName, Positioning, StyleSummary and timestamps.
- CreatorStyleEvidence: profile/experiment/winner IDs; experiment and variant names; hook, speaking style, editing pace, audio approach; metrics, follows, clicks, orders and watch-time values.
- Evidence snapshots the confirmed winner. Losing variants remain in StyleExperimentVariants for analysis.
- Unique (CreatorStyleProfileId, StyleExperimentId) prevents duplicate evidence imports.

## 3. Relationship summary

~~~mermaid
erDiagram
    ContentProjects ||--o{ StyleExperiments : "optional project link"
    ContentTopics ||--o{ DouyinVideoCandidates : classifies
    DouyinSourceChannels ||--o{ DouyinVideoCandidates : source
    VietnamTrendSignals ||--o{ VietnamTrendMetricSnapshots : observes
    StyleExperiments ||--|{ StyleExperimentVariants : contains
    StyleExperimentVariants ||--o| StyleExperiments : "confirmed winner"
    CreatorStyleProfiles ||--o{ CreatorStyleEvidence : accumulates
    StyleExperiments ||--o{ CreatorStyleEvidence : evidenced_by
    StyleExperimentVariants ||--o{ CreatorStyleEvidence : winning_snapshot
~~~

## 4. Integrity and deletion behavior
- Deleting a Douyin channel or topic sets the optional candidate FK to NULL.
- Deleting a content project sets an experiment's optional project FK to NULL.
- Deleting a trend signal cascades to metric snapshots.
- Deleting a style profile cascades to its evidence.
- Variants cascade from their experiment.
- Evidence references experiments and variants with no-action delete behavior, preserving historical provenance while evidence exists.
- Metrics are non-negative; watch-time values are non-negative and average watch time cannot exceed video duration when both are present.

## 5. Metric observation history
Migration 008 adds immutable StyleVariantMetricObservations. Every successful variant measurement appends a row with all eight count metrics, watch time, duration and ObservedAt. The variant row remains the latest summary for lifecycle/API compatibility. Observation-derived rates include engagement, follow, product-click, order and watch-retention rates. Repeated measurements are accepted only at a later timestamp while the experiment remains Running.

## 6. Topic migration
007_style_learning_topic.sql adds nullable StyleExperiments.Topic nvarchar(200) and an index on (Topic, Status, UpdatedAt DESC). Existing rows remain NULL; no historical topic is guessed from names or scripts.

## 7. Known model limitations
- No user, tenant, account ownership or role tables isolate creator data.
- ProfileKey is an application-level key, not a foreign key to an authenticated user.
- Experiment topic is one optional free-text value, not a normalized topic FK or multi-label taxonomy.
- Trend observations lack source connector run IDs, licensing metadata and connector health.
- No normalized media asset, product association, TikTok publication or analytics-sync entities exist yet.

## 7. Learning Memory (migration 009)
- `StyleLearningProposals` stores the proposed summary, source experiment IDs, immutable evidence JSON snapshot, algorithm version, status, reviewer, reason and review timestamps.
- A proposal begins Pending and can transition exactly once to Approved or Rejected. Rejection requires a reason.
- `CreatorStyleProfileVersions` stores a complete immutable copy of display name, positioning and style summary. Approval updates current guidance and inserts the next version in the same SaveChanges transaction.
- The current API caller supplies `reviewerId`; this is audit metadata only, not a verified identity. User/tenant authorization remains a release blocker before multi-creator use.


## Trend Selection feedback and outcomes (migration 010)

- Feedback stores the creator-authored decision separately from model features. Empty reason notes are valid; Select/Reject/Watch are the only decisions.
- Outcome observations are separate rows linked to a feedback decision. Their source and observed time are preserved with a JSON metrics snapshot.
- Both tables are append-only at the SQL Server boundary through DML triggers. Migration index creation is independently guarded so a partially applied migration can be rerun.
- Feature snapshots currently use an empty object placeholder; do not train a preference model until a versioned extractor has populated real feature vectors.


## SaaS identity foundation (migration 011)
- `Tenants` stores workspace identity, unique normalized slug and Active/Suspended lifecycle.
- `TenantMemberships` maps immutable external identity issuer + subject to a tenant role and membership status. The issuer/subject pair is not an email address and must come from a validated OIDC principal.
- Migration 011 deliberately does not backfill or add `TenantId` to existing content, trend, experiment or profile tables. This avoids inventing ownership for existing data.
- No tenant-facing business endpoints should be enabled until OIDC validation, membership resolution, row ownership, query scoping and cross-tenant negative tests are implemented.

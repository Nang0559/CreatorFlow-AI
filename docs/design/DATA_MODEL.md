# Data model — implemented schema

This document records the current SQL Server/EF Core model. Numbered scripts under database/schema are the DDL deployment sequence; CreatorFlowDbContext contains the EF Core mappings.

## 1. Entity map

| Entity/table | Purpose | Relationships and constraints |
|---|---|---|
| ContentProjects | Work container for a content idea/production | Optional reference from StyleExperiments; deleting project sets link to NULL |
| ContentTopics | Creator-defined topic and keywords | Can classify a Douyin candidate |
| DouyinSourceChannels | Douyin creator/channel metadata and follow state | Unique DouyinSecUserId; index on follow state/last checked |
| DouyinVideoCandidates | Discovered or manually submitted video metadata | Unique DouyinVideoId; optional channel/topic references |
| VietnamTrendSignals | Vietnam creator/video/sound trend identity and latest metrics | Unique platform + kind + external ID |
| VietnamTrendMetricSnapshots | Immutable trend metric observations | FK to trend signal, cascade delete; unique signal + observation time |
| StyleExperiments | Hypothesis and 2–3 variant comparison | Optional project FK, optional topic, optional winning variant |
| StyleExperimentVariants | Creative variant and measured metrics | FK to experiment; unique experiment + variant name |
| CreatorStyleProfiles | Creator positioning and editable guidance | Unique ProfileKey |
| CreatorStyleEvidence | Snapshot of confirmed winner and measured results | FK to profile, experiment and winner; unique profile + experiment |

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

## 5. Topic migration
007_style_learning_topic.sql adds nullable StyleExperiments.Topic nvarchar(200) and an index on (Topic, Status, UpdatedAt DESC). Existing rows remain NULL; no historical topic is guessed from names or scripts.

## 6. Known model limitations
- No user, tenant, account ownership or role tables isolate creator data.
- ProfileKey is an application-level key, not a foreign key to an authenticated user.
- Experiment topic is one optional free-text value, not a normalized topic FK or multi-label taxonomy.
- Trend observations lack source connector run IDs, licensing metadata and connector health.
- No normalized media asset, product association, TikTok publication or analytics-sync entities exist yet.

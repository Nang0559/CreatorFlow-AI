# CreatorFlow content workflow

## Product intent

CreatorFlow is a Douyin-to-Vietnam content intelligence and production workspace. The creator chooses topics, discovers Chinese-market videos, follows source channels, selects promising source videos, adapts the ideas and production into Vietnamese-market content, then reviews and publishes to their own TikTok Vietnam channel with relevant products where the account and platform features permit.

The source video is an input and reference for a new localized production workflow. A source URL and its metadata are not the same thing as a downloaded media file, and a content project is not merely a blank idea.

## Discovery paths

| Path | User action | Stored record |
|---|---|---|
| Topic exploration | Select a topic and search/browse Douyin videos by keywords or hashtags | Content topic and discovered video candidate (TopicExplore) |
| Followed channel | Save a Douyin creator, mark it followed, periodically review new posts | Source channel and video candidate (FollowedChannel) |
| Manual intake | Paste a Douyin video link found elsewhere | Video candidate (Manual) |

A video candidate is deduplicated by its Douyin video identifier. The same video discovered through more than one path should remain one candidate; discovery provenance can be expanded into a separate event table when connector ingestion is implemented.

## Adaptation lifecycle

Candidate -> Selected -> QueuedForAdaptation -> Adapted

A candidate can also be Rejected. Only selected candidates can enter the adaptation queue. A queued or adapted candidate cannot be silently rejected. The future adaptation project should retain links to its source candidate and record localized scripts, edit instructions, media assets, render versions, product associations, review decisions and publication identifiers.

## Production and commerce stages

| Stage | Input | Output | Gate |
|---|---|---|---|
| Source analysis | Candidate video metadata and user notes | Story structure, hook, visual sequence and product context | Creator chooses what to adapt |
| Localization | Analysis and Vietnamese audience brief | Vietnamese angle, rewritten script, captions and narration | Human script review |
| Production | Approved localized script and available assets | Versioned edit plan and rendered video | Media validation and creator review |
| Product matching | Localized content and seller catalog | Product association and selling points | Product accuracy and account eligibility |
| Publication | Approved video and post metadata | TikTok post reference | Explicit creator confirmation and supported publishing method |
| Measurement | Platform-provided metrics | Performance snapshot | Respect platform permissions and retention |

## Integration boundary

The current domain and SQL schema provide discovery/selection metadata foundations. A creator can optionally share a URL into the tenant-scoped Share Inbox; this raises priority but is not a prerequisite for topic-driven discovery. The Share Inbox stores the submitted URL only and does not resolve/fetch it. No live Douyin/TikTok provider currently collects candidates automatically. CreatorFlow does not log in to Douyin, scrape feeds, follow accounts on-platform, publish to TikTok, or attach products to a shopping cart. Those capabilities require separately implemented and tested connectors, with platform access, account eligibility, and applicable content-use requirements verified before activation. Do not treat a browser-visible page or a public URL as proof that an automated API is available.

Jobs should preserve successful artifacts on failure. Idempotency keys should prevent duplicate candidate ingestion, generated artifacts and external posts.

## Vietnam market intelligence

The Vietnam market is tracked separately from Douyin source discovery. A trend signal can represent a creator channel, a video, or a sound/music item on TikTok Vietnam, YouTube Shorts Vietnam, Facebook Reels Vietnam, or another explicitly recorded platform. Each observation stores its source URL, topic, creator/sound attribution and a timestamped snapshot of views, likes, comments, shares and saves. Repeated observations must be chronological; this makes trend acceleration measurable instead of treating a single view count as a trend score.

These records are observations entered by a user or supplied by a configured connector. The schema does not imply that TikTok/Meta/YouTube trend feeds or music charts are already connected.

## Style Lab: controlled 2–3 variant tests

A style experiment is attached optionally to a content project and requires exactly two or three distinct variants. Each variant records:
- Hook/opening approach and script;
- Vietnamese speaking/narration style;
- Editing pace and visual rhythm;
- Audio/music approach and creative notes;
- Publication timestamp and measured views, likes, comments, shares, saves, follows gained, product clicks, orders, average watch time and video duration.

An experiment can start only after every variant is ready. It can be evaluated only after every variant has been published and measured; the creator explicitly records the winning variant. Engagement rate is exposed as a derived descriptive metric, not an automatic winner score. Reach, retention, follows and commerce outcomes should be considered against the experiment's stated hypothesis and comparable measurement windows.

The goal is cumulative learning: preserve winning and losing experiments, import confirmed winners into a creator style profile, submit evidence-grounded synthesis proposals for human review, and test whether approved traits keep working across topics and products. Do not copy source dialogue or reproduce a source video shot-for-shot; translate the underlying creative mechanism into a distinct Vietnamese execution.


## Creator style profile and cumulative learning

A creator style profile is created for a stable creator/account key. It contains positioning and editable style guidance, plus an evidence history of winning variants from completed Style Lab experiments.

Learning is explicit:
1. Complete and measure all two or three variants in an experiment.
2. The creator reviews the evidence and confirms the winning variant.
3. The creator explicitly adds that evaluated experiment to a style profile.
4. CreatorFlow stores a snapshot of the winning hook, speaking style, editing pace, audio approach and measured results.
5. After several experiments, an AI synthesis step can identify repeated traits across topics and product categories and propose an updated style summary for creator approval.

One successful video is evidence, not a permanent style rule. Preserve counterexamples and losing variants in their original experiments. Compare outcomes over similar measurement windows and consider retention, follows and commerce outcomes alongside engagement. The style summary is editable and should not be overwritten by an unreviewed AI inference.

The current `ProfileKey` is unique within a tenant but is not a foreign key to a specific authenticated user. Tenant membership and data filters exist as foundations; production OIDC configuration, complete endpoint role policies and cross-tenant negative tests remain release gates.


## As-built design references and current learning behavior

The implemented Style Lab and creator-style learning behavior is specified in [Style Lab and Style Learning design](design/STYLE_LAB_AND_LEARNING.md). The implemented entities and relationships are specified in [Data model](design/DATA_MODEL.md).

Current Style Learning is a deterministic analysis of evaluated 2–3-variant experiments. It requires every variant to be measured and meet the minimum-view threshold, compares exact repeated hook/speaking/edit/audio values, and only surfaces factors with at least two decisive supporting experiments, at least two wins and positive lift over the mean 1/N baseline. Factors present in both winner and loser variants of the same experiment are treated as ambiguous for that experiment. The analysis is descriptive, not causal, and is not an ML/LLM-generated conclusion.

Important scope limitation: the learning-analysis route verifies the profile exists but currently analyzes the evaluated experiment pool, not only experiments imported into that profile. It must not be treated as profile isolation or multi-account-safe behavior. Learning Memory separately persists submitted proposals, reviewer identity, approval/rejection state and immutable profile versions. The deterministic learning-analysis endpoint still reads the global evaluated experiment pool rather than only evidence imported into the requested profile; this is a multi-tenant release blocker.


## Initial automated video rendering

The Assets page accepts user-provided MP4/MOV/M4V source media (up to 500 MB) after an explicit rights confirmation. The confirmation is not legal verification. A creator can choose two or three different render styles; the API commits the whole group of SQL jobs atomically. A local Windows worker uses After Effects/aerender for a vertical 1080×1920 composition and PNG sequence, then FFmpeg/FFprobe to produce and validate H.264/AAC MP4 outputs. CreatorFlow provides progress, preview and download. This initial renderer adds title/hook/CTA treatments; advanced effects, B-roll, STT, TTS, beat-synced music and platform publishing remain future work. Hardware acceptance on the HP Z240 is still required.

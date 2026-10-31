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

The current domain and SQL schema provide the discovery and selection foundation only. They do not yet log in to Douyin, scrape feeds, follow accounts, download media, publish to TikTok, or attach products to a shopping cart. Those capabilities require separately implemented and tested connectors, with platform access, account eligibility, and applicable content-use requirements verified before activation. Do not treat a browser-visible page or a public URL as proof that an automated API is available.

Jobs should preserve successful artifacts on failure. Idempotency keys should prevent duplicate candidate ingestion, generated artifacts and external posts.
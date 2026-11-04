# Discovery connectors: public web + approved TikTok API

## What is implemented

The authenticated endpoint `POST /api/v1/discovery/search` accepts the AI query plan and calls two independent providers:

- **Brave Search Web API**: queries its public web index with Vietnam country/language hints and a TikTok profile domain filter. Only HTTPS results on `tiktok.com` or a TikTok subdomain with a parseable `/@username` path are accepted. The original result URL is normalized to a canonical profile/video URL; title and snippet remain attributed to Brave.
- **TikTok Research API**: obtains a client-credentials token and queries public video metadata constrained to `region_code=VN` and the requested keyword. It constructs canonical video/profile links from the returned video ID and username. This connector is used only when credentials are present and the TikTok Research project has been approved.

The endpoint returns provider status independently, actual channel/video URLs, and only metrics supplied by TikTok. It does not fabricate counts, infer followers, or treat an AI query plan as a result.

## Important TikTok access boundary

Research API access is restricted to approved research projects and approved purposes. A normal TikTok developer app or Display API approval does not automatically grant public-wide discovery. Display API is for a creator-authorized user's profile and videos; it is not a general creator search endpoint.

If the Research API project is not approved, leave the credentials blank. Brave public web search can still return indexed public TikTok pages, but it is not complete coverage and results can be stale or unavailable. Do not replace this with scraping, browser automation, cookies, QR login, or undocumented endpoints.

## Configure in the workspace

Open **Cấu hình hệ thống** (`/settings`) and enter the Brave Search API key in **Khóa kết nối khám phá**. TikTok Research Client Key and Client Secret are optional and should only be entered after the project is approved. The API encrypts these values with ASP.NET Core Data Protection and stores them per workspace in SQL Server. Only workspace Owner/Admin can read credential-presence status, replace keys, or clear them; the raw secrets are never returned to the browser. The API's Data Protection key ring must remain persistent and protected on the server.

Environment variables remain supported as a server-level fallback for existing deployments. Do not put real keys in Git or browser-side configuration.

## Configure on the Z240 (legacy environment fallback)

The API is currently run as a Windows process, separate from Docker. If you prefer environment configuration, set secrets in the same PowerShell window that starts the API:

```powershell
cd E:\Projects\CreatorFlow-AI

# Required for public indexed web search. Obtain a key from the Brave Search API dashboard.
$env:BraveSearch__ApiKey = "YOUR_BRAVE_SEARCH_API_KEY"

# Optional. Only configure after TikTok has approved a Research API project.
$env:TikTokResearch__ClientKey = "YOUR_TIKTOK_RESEARCH_CLIENT_KEY"
$env:TikTokResearch__ClientSecret = "YOUR_TIKTOK_RESEARCH_CLIENT_SECRET"

dotnet run --no-build --project .\src\CreatorFlow.Api\CreatorFlow.Api.csproj --no-launch-profile
```

Restart the API process after changing any of these variables. The Web container does not need the search keys because all provider calls are made server-side by the Windows API.

## UI flow

1. Create/select a saved topic.
2. Click **Tìm kênh theo chủ đề** to ask the configured Ollama/AI provider for query terms. The UI then automatically calls the connector endpoint.
3. Review provider status, profile URLs, video URLs, source attribution and any metrics returned by the provider.

An empty result is a valid result. It means no matching, parseable TikTok pages were returned for the queries, or the source is not configured/approved. The UI must not create fallback channel cards.

## Operational limitations

- Current search is an on-demand UI operation. It does not yet persist discovered channels/videos or make the recurring topic scheduler execute connector searches.
- Brave result URLs are evidence of indexed pages, not a guarantee that the page remains live or belongs to a creator in Vietnam. Validate the profile before following it.
- TikTok Research API's date window is limited to the recent 30 days in this first implementation.
- No media is downloaded. Metadata visibility is not media-download permission.
- API keys remain server-side. Never send them to the Web UI or commit them.

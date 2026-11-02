# HP Z240 – one-run local setup

## Preconditions
- Windows PowerShell 7 or Windows PowerShell 5.1.
- Repository cloned at `E:\Projects\CreatorFlow-AI`, branch `feature/trend-selection-safety-intelligence`.
- Existing local SQL Server database `CreatorFlow_QC` is ONLINE and writable, and already tagged as CreatorFlow-AI QC.
- Docker Desktop is running.
- A valid local `.env` exists. Do not commit it or share its secrets.
- PowerShell module `SqlServer` is installed.

## One command
Open PowerShell in the repository root and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Setup-HPZ240.ps1
```

The script checks the branch and clean working tree, fast-forward pulls the branch, validates local configuration without printing secrets, applies only missing SQL migrations 001–010 and 014–016 using the checksum ledger, verifies the QC database, restores/builds/tests .NET, builds/starts Docker AI and Web, waits for AI health/readiness, starts or reuses the native API on port 7001, and reports CPU/RAM/GPU plus whether CapCut Desktop and After Effects are installed. It then probes the local Web UI at port 8080. By default, Web is accessible only from the HP itself.

## Safe reruns
- Already-applied SQL migrations are skipped only if their stored SHA-256 matches the repository file.
- Existing healthy API on port 7001 is reused; an unrelated process occupying that port causes a safe stop.
- Docker Compose uses `up --build -d`; it does not delete volumes.
- The script never runs `git reset`, force-push, database drop/restore, or process termination.
- If local Git changes exist, it stops before pulling. Commit or back up those changes, then rerun.
- If a migration checksum differs, do not edit the old migration; add a new numbered migration after investigation.

## Optional switches
- `-SkipGitPull`: use the already checked-out commit.
- `-SkipDotNetTests`: build but skip .NET tests.
- `-SkipDocker`: skip Docker build/start and AI checks.
- `-SkipApiStart`: do not start/reuse the native API.
- `-HealthTimeoutSeconds 180`: allow more time for first startup.
- `-EnableLanAccess`: optional; publishes Web port 8080 to the LAN and adds a Windows Firewall rule limited to Private profile + LocalSubnet. Requires elevated PowerShell and a trusted Private network. SQL (1433), API (7001) and AI (8001) are not published.
- `-ConfirmLegacyWorkspaceAssignment`: only after reviewing migrations 011–013, explicitly authorize applying tenant ownership/lifecycle migrations to existing QC business rows. If migrations 011–013 are already installed, this is not needed.

## Expected URLs
- On the HP: http://localhost:8080
- On a phone on the same trusted Wi-Fi/LAN, when `-EnableLanAccess` is used: open the `Phone URL` printed by the script (for example `http://192.168.x.x:8080`).
- API: http://localhost:7001/health
- AI: http://localhost:8001/health and http://localhost:8001/ready

## Security
The setup preserves the phone-only Douyin rule. It does not log in to Douyin/TikTok, scan login QR codes, create a browser/device session, access cookies, follow, like, comment, or publish. The AI discovery service may only use sources separately reviewed and enabled for permitted access.

## Important boundary
A successful local setup proves database migrations, builds, container startup and health endpoints. It does not prove that live Douyin/TikTok discovery providers are connected or that feedback is already persisted end-to-end; those require their own integration and acceptance tests.

## Share links from iPhone
The Trends page accepts pasted links. For one-action share-sheet handoff, create the iOS Shortcut described in `docs/operations/IOS_SHARE_TO_CREATORFLOW.md`; iOS Safari does not support PWA share-target registration. The shortcut opens the HP's LAN URL with the shared link, which the page stores in Share Inbox.

## Video editing applications
The setup script detects CapCut Desktop and After Effects but does not install them or sign into any editor account. Install CapCut from [the official Desktop page](https://www.capcut.com/tools/desktop-video-editor). After Effects is optional and requires an Adobe license; see [Adobe After Effects](https://www.adobe.com/products/aftereffects.html). Style Lab can export a CapCut/After Effects handoff ZIP containing the script, shot list, edit plan and an editable After Effects starter composition. This is not a finished MP4 or a native CapCut project. See `docs/design/VIDEO_EDITING_HANDOFF.md`.

## Mobile-first interface
The same setup pulls the responsive Web UI. On phone-sized screens it switches to a compact top bar and five-item bottom navigation: Overview, Discover, Studio, Performance, Settings. Desktop retains the sidebar and secondary management links. Touch targets, input sizing, safe-area spacing for iPhone, compact cards and reduced-motion preferences are included. The home page prioritizes three common actions: discover content, create style variants, and review performance. The interface does not claim that unimplemented integrations are connected.

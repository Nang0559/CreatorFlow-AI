# CreatorFlow video editing handoff

## Product decision
CreatorFlow is the planning, intelligence, asset-manifest and learning layer. It is not intended to replace a professional nonlinear editor with a low-quality in-house renderer.

## Supported workflow in this release
1. Generate and review 2–3 distinct creative variants in Style Lab.
2. Download an authenticated, tenant-scoped editing package for each variant.
3. The ZIP contains:
   - `edit-plan.json`: 9:16 project profile and all creative decisions.
   - `script.txt`: approved working script.
   - `shot-list.csv`: editable shot prompts and pacing notes.
   - `README-CapCut-AfterEffects.txt`: handoff steps.
   - `after-effects/CreatorFlow-AE-Starter.jsx`: creates an editable 1080x1920, 30 fps composition with text and shot placeholders.
4. In CapCut Desktop, import original/licensed footage and assemble using the shot list; apply the effects, transitions, typography, color and sound design manually.
5. In After Effects, run the JSX starter, replace placeholders with source media, add licensed plugins/effects, then render from AE.

This is a handoff package and editable AE starter, not a finished MP4, not a native CapCut project, and not a replacement for the editor's effect library.

## Why CapCut is not automated by editing private draft files
CapCut does not provide a public, stable desktop scripting API documented for this integration. Writing undocumented internal draft JSON or using unofficial reverse-engineered tools risks breaking projects when CapCut updates. CreatorFlow therefore uses open media and text handoff formats and lets CapCut own the timeline/effects. The user remains in control of the editor and its account.

## After Effects automation
Adobe documents `afterfx.exe -r <script.jsx>` and `aerender.exe` for scripted work and command-line rendering. The current starter creates the editable composition; unattended render-queue automation is a later phase after project templates, footage mapping, fonts, plugins, codecs and output validation are tested on the actual workstation.

## HP Z240 installation
- Install CapCut Desktop from the official download page: https://www.capcut.com/download
- After Effects is optional and requires the user's Adobe account/license: https://www.adobe.com/products/aftereffects.html
- Do not put editor credentials, cookies or account tokens into CreatorFlow.
- The HP setup script only detects CapCut and After Effects; it does not silently install software, sign in, or accept license terms.
- The HP Z240's exact GPU is not yet confirmed. Run the setup script and review its CPU/RAM/GPU report before deciding whether AE previews and full-resolution renders are practical. Start with proxy/preview quality if the GPU is limited.

## Go-live acceptance for video editing
- [ ] Downloaded ZIP opens and all expected files are present.
- [ ] AE JSX creates a project that opens without script errors.
- [ ] Footage placeholders can be replaced and the AEP project saved.
- [ ] CapCut import and manual edit are verified on the installed CapCut version.
- [ ] Test 1080x1920 export with sound, subtitles, safe areas and product CTA.
- [ ] Verify output file codec, duration, dimensions, audio sync and playback.
- [ ] Compare render time and visual quality on the actual Z240.
- [ ] No platform watermark, unlicensed music, copied third-party footage or unverified product claims.

# Receive a Douyin/TikTok share on iPhone

## Why a Shortcut is used
An iOS web app cannot register itself as a native receiving destination in the iOS share sheet through the Web Share Target API. Apple's Shortcuts app can receive a URL from the share sheet and open CreatorFlow with that URL. This keeps CreatorFlow as a web app and does not require a Douyin login on the HP.

## One-time setup
1. First run the HP setup with `-EnableLanAccess` on a trusted Private Wi-Fi network.
2. Note the `Phone URL` printed by the setup script, for example `http://192.168.1.20:8080`. The HP and iPhone must remain on the same trusted Wi-Fi. If the router changes the HP's IP, update the shortcut or reserve the IP in the router.
3. On iPhone, open **Shortcuts** and create a new shortcut named **CreatorFlow – Nhận link**.
4. Open shortcut Details and enable **Show in Share Sheet**. Set accepted input to **URLs** (or URLs and Text if Douyin sends a text block containing the URL).
5. Add these actions in order:
   - **Get URLs from Shortcut Input**.
   - **Get Item from List** → First Item (if the previous action returns more than one URL).
   - **URL Encode** the selected URL.
   - **Text**: `http://192.168.1.20:8080/trends?sharedUrl=` followed by the encoded URL variable. Replace the example IP with the HP's current Phone URL host.
   - **Open URLs** using that Text result.
6. Save the shortcut.

## Daily use
1. In Douyin, open the channel or video and tap Share.
2. Choose **CreatorFlow – Nhận link** in the iOS share sheet.
3. CreatorFlow opens on the HP's local Web UI and automatically saves the shared link into Share Inbox.
4. The link is a priority signal only. It does not log into Douyin, follow/unfollow, like, comment, download or publish.

If the shortcut is unavailable for a particular Douyin share action, use Copy Link, return to CreatorFlow → Xu hướng Việt Nam, paste into the Share Inbox field and tap **Nhận link chia sẻ**.

## Security and network limits
- This local shortcut works only while the iPhone can reach the HP on the same trusted LAN.
- The setup exposes only Web TCP 8080 to LocalSubnet on a Windows Private network. It does not publish SQL 1433, API 7001 or AI 8001.
- Do not enable this on a Public or untrusted Wi-Fi network.
- The link is validated by allowed HTTPS source domains and stored as user-provided metadata. The server does not fetch the supplied URL.

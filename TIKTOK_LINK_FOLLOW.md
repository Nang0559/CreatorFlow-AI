# Theo dõi kênh TikTok bằng link chia sẻ

Tính năng này cho phép bạn dán (hoặc chia sẻ từ điện thoại) một liên kết TikTok để CreatorFlow **ghi nhận kênh, đặt kênh ở trạng thái đang theo dõi và lưu thông tin video**. Mỗi video bạn chia sẻ sau đó được gắn vào đúng kênh, kèm nhãn “mới” trong 3 ngày.

## Tính năng làm gì và không làm gì
Có làm:
- Nhận link video (`https://www.tiktok.com/@tenkenh/video/...`), link kênh (`https://www.tiktok.com/@tenkenh`) hoặc link rút gọn (`vm.tiktok.com/...`).
- Lấy tên kênh và tiêu đề video từ oEmbed công khai của TikTok (không cần khóa API).
- Tạo kênh nếu chưa có, bật theo dõi, ghi nhận video (không trùng) và đưa link vào Share Inbox ở trạng thái đã gắn kênh.

Không làm (có chủ đích):
- Không tải video, không gỡ watermark, không đăng nhập hay dùng cookie TikTok.
- Không tự phát hiện video mới của kênh. Video chỉ xuất hiện khi bạn chia sẻ link. Việc nhận thông báo video mới do chính TikTok (bật chuông thông báo trên kênh) đảm nhiệm.
- Không tự quét kênh: kênh tạo bằng cách này không có lịch quét trong `DiscoveryChannelSyncStates`.

## Cách dùng
**Trên máy tính.** Mở **Kênh theo dõi** → ô **Theo dõi kênh từ link TikTok** → dán link → **Theo dõi kênh từ link**. Thông báo cho biết kênh mới hay đã có, video mới hay đã ghi nhận.

**Trên iPhone (một chạm).** Tạo Shortcut theo `IOS_SHARE_TO_CREATORFLOW.md`, nhưng dòng **Text** dùng đường dẫn:
`http://<IP-của-HP>:8080/channels?link=` + biến URL đã mã hóa. Khi chia sẻ video TikTok → chọn Shortcut, trang Kênh theo dõi mở ra và tự xử lý link.

**Trên Android.** Chọn Chia sẻ → Sao chép liên kết, mở CreatorFlow → dán vào ô nói trên.

**Xem video đã ghi nhận.** Mỗi kênh hiển thị số video, số video mới trong 3 ngày và nút **Xem video đã ghi nhận** (20 video gần nhất, có link mở trên TikTok).

**Sau đó.** Khi bạn có file video hợp lệ (của bạn, có giấy phép hoặc chủ kênh đồng ý), tải lên ở **Video → Assets** kèm xác nhận quyền để dựng 3 phong cách.

## Link nào dùng được
| Dạng link | Kết quả |
|---|---|
| `tiktok.com/@kênh/video/<id>` | Nhận kênh và video từ chính đường dẫn; oEmbed bổ sung tên kênh, tiêu đề. oEmbed lỗi vẫn dùng được (tên hiển thị là `@kênh`). |
| `tiktok.com/@kênh` | Chỉ theo dõi kênh, không có video. |
| `vm.tiktok.com/...`, `tiktok.com/t/...` | Cần oEmbed để biết kênh. Nếu lỗi, mở link trong trình duyệt và sao chép link đầy đủ. |
| Link không thuộc tiktok.com, HTTP, có cổng hoặc có user:pass | Bị từ chối (HTTP 400). |

## Dữ liệu được lưu
- Kênh: bảng `DouyinSourceChannels`, `DouyinSecUserId = tiktok:<handle viết thường>`, `ProfileUrl = https://www.tiktok.com/@<handle>`. Tên bảng là di sản “Douyin”; không cần migration mới.
- Video: bảng `DouyinVideoCandidates`, `DouyinVideoId = tiktok:<id>`, `DiscoveryMethod = FollowedChannel`, trạng thái `Candidate`.
- Link: bảng `SharedLinkInboxItems`, trạng thái `Linked`, `LinkedChannelId` trỏ tới kênh. Domain `LinkChannel` nay chấp nhận cả nền tảng TikTok.

## API
| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| POST | `/api/v1/douyin/channels/from-link` | Body `{ "url": "..." }`. Trả `channelId`, `handle`, `displayName`, `channelCreated`, `followed`, `videoId`, `videoRecorded`, `videoAlreadyKnown`, `metadataSource` (`oembed` hoặc `link`). |
| GET | `/api/v1/douyin/channels/{id}/videos?take=20` | Video đã ghi nhận của kênh (1–100). |
| GET | `/api/v1/douyin/channels/activity` | Mỗi kênh: tổng video, thời điểm gần nhất, số video mới trong 3 ngày. |

Quyền: cùng chính sách với `/api/v1/douyin/channels` (đăng nhập; vai trò chỉ đọc không được ghi).

## Bảo mật
- Máy chủ **không bao giờ truy cập URL do người dùng nhập**. Chỉ gọi `https://www.tiktok.com/oembed?url=<URL đã mã hóa>` tới host cố định.
- HTTP client riêng `tiktok-oembed`: không theo chuyển hướng, không cookie, timeout 8 giây, giới hạn phản hồi 64 KB.
- Chỉ chấp nhận HTTPS, cổng mặc định, không thông tin đăng nhập nhúng, host `tiktok.com` hoặc `*.tiktok.com`. Handle phải khớp `^[A-Za-z0-9_.]{2,32}$`.
- Dữ liệu theo tenant như các endpoint kênh hiện có.

## Kiểm thử
Tự động (chạy trong CI hoặc trên máy có .NET SDK 9):
```powershell
dotnet test tests/CreatorFlow.Domain.Tests --filter SharedLinkInboxItemTests
dotnet test tests/CreatorFlow.Api.Tests --filter "TikTokLink"
```
Các test endpoint dùng EF InMemory và oEmbed giả, không gọi mạng ngoài; hành vi SQL Server (unique index) vẫn cần chạy QC.

Thủ công trên máy chạy thật:
1. Dán `https://www.tiktok.com/@<kênh>/video/<id>` → kênh xuất hiện, trạng thái Đang theo dõi, “Video đã ghi nhận: 1”.
2. Dán lại cùng link → thông báo đã ghi nhận trước đó, số video không đổi.
3. Dán link kênh → chỉ theo dõi kênh.
4. Dán link YouTube hoặc `http://` → báo link chưa hợp lệ.

## Xử lý sự cố
- “Không xác định được kênh”: dùng link đầy đủ có `@tênkênh`.
- Tên kênh hiện `@handle`, không có tiêu đề: oEmbed không trả dữ liệu (mạng chặn, TikTok giới hạn). Dữ liệu từ đường dẫn vẫn được lưu.
- Shortcut không mở đúng trang: kiểm tra IP của HP và dùng `/channels?link=` thay cho `/trends?sharedUrl=`.

## Giới hạn đã biết
- Chưa kiểm chứng với oEmbed thật của TikTok: cấu trúc phản hồi có thể khác (trường `author_unique_id`, `embed_product_id` không bảo đảm có). Lỗi chỉ làm mất phần bổ sung, không làm hỏng việc ghi nhận từ đường dẫn đầy đủ.
- Nhãn “mới” = video ở trạng thái Candidate được ghi nhận trong 3 ngày qua.
- Muốn tự động phát hiện và lấy video của kênh có quyền (kênh của bạn hoặc người hợp tác đồng ý), cần connector riêng dùng TikTok Login Kit và Display API, không thuộc patch này.

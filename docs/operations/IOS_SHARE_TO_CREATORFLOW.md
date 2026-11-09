# CreatorFlow – Tải video TikTok (iPhone)

## Mục tiêu giai đoạn 1

Phím tắt nhận URL TikTok từ Share Sheet, gọi endpoint phân giải công khai, tải trực tiếp URL CDN HTTPS được trả về và lưu tệp video vào Photos. Giai đoạn này **không** gửi tệp lên Share Inbox, không tạo render jobs và không cần đăng nhập CreatorFlow.

## Endpoint

- iPhone-facing URL: `https://CreatorFlow.fcc-vn.com.vn/public-media/tiktok/resolve?url=<URL_ENCODED_TIKTOK_URL>`
- Web BFF chuyển tiếp nội bộ đến API `/api/v1/public-media/tiktok/resolve`. Chỉ công khai đúng route này; không công khai API 7001, AI 8001 hoặc SQL 1433.
- API trả JSON gồm `success`, `title`, `mediaUrl`, `contentType`. Phím tắt tải giá trị `mediaUrl` trực tiếp từ CDN TikTok và lưu vào Photos.

## Tạo Shortcut trên iPhone

1. Mở Shortcuts → dấu + → đặt tên chính xác **CreatorFlow – Tải video TikTok**.
2. Mở Details → bật **Show in Share Sheet**. Trong Accepted Types chỉ bật **URLs**.
3. Thêm action **Get URLs from Shortcut Input**.
4. Thêm **Get Item from List**, chọn First Item.
5. Thêm **URL Encode** cho URL TikTok (nếu action hỏi kiểu encode, chọn Encode).
6. Thêm action **URL** với mẫu:
   `https://CreatorFlow.fcc-vn.com.vn/public-media/tiktok/resolve?url=[Encoded URL]`
   Chèn biến Encoded URL vào cuối query, không gõ dấu ngoặc vuông.
7. Thêm **Get Contents of URL** (GET). Kết quả là Dictionary.
8. Thêm **Get Dictionary Value**, key `success`; thêm **If**. Nếu không phải true, dùng **Show Alert** với thông báo phân giải thất bại rồi **Stop This Shortcut**.
9. Trong nhánh Otherwise, thêm **Get Dictionary Value**, key `mediaUrl`.
10. Thêm **Get Contents of URL** với biến mediaUrl. Đây là bước tải byte video từ CDN, không phải tải JSON.
11. Thêm **Save to Photo Album**, chọn Recents (hoặc album video bạn muốn).
12. Thêm **Show Notification**: “CreatorFlow: Đã lưu video vào Ảnh”.

## Kiểm thử và giới hạn

- Thử trước với video TikTok công khai, không yêu cầu đăng nhập.
- Mở Photos → Recents và phát video vừa lưu. Chỉ coi thành công nếu tệp phát được và có thời lượng/dung lượng hợp lý.
- Nếu API trả 422, trang không công khai URL media trực tiếp theo metadata được hỗ trợ. Không dùng thủ thuật vượt login, CAPTCHA, DRM hoặc anti-bot.
- URL CDN có thể hết hạn hoặc yêu cầu header/phiên mà Shortcuts không có; khi đó tải sẽ thất bại. Đây là giới hạn thực tế của cách tải trực tiếp, không phải lỗi lưu Photos.
- Endpoint trả media URL tạm thời, không lưu URL hoặc video vào database. Có rate limit và chỉ chấp nhận HTTPS host TikTok/CDN trong allowlist.

## Giai đoạn 2 (chưa bật)

Sau khi xác nhận tải Photos ổn định, mới bổ sung hành động gửi tệp lên CreatorFlow, lưu Share Inbox, xác nhận quyền và tạo đúng 3 render jobs. Không tự động dựng video trong giai đoạn 1.


## Trang tải công khai từ Share Sheet

- Shortcut nhận URL TikTok và mở `https://CreatorFlow.fcc-vn.com.vn/public-media/tiktok?url=<URL_ENCODED>`.
- Trang tự phân giải và hiển thị tiêu đề, ảnh bìa (nếu có) cùng định dạng MP4 mà metadata công khai xác nhận.
- Nút tải mở URL media CDN. Trên iPhone Safari thường lưu vào Files; để lưu trực tiếp Photos cần Shortcut riêng dùng Get Contents of URL → Save to Photo Album.
- Không gắn nhãn HD/SD/không watermark nếu resolver không xác minh được các biến thể. Bản này chỉ hiển thị MP4 nguồn công khai.

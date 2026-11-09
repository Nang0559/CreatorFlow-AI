# CreatorFlow – Tải video TikTok (iPhone)

## Mục tiêu

Từ TikTok, người dùng chọn Chia sẻ → **CreatorFlow – Tải video**. Shortcut nhận URL và mở trang tải CreatorFlow. Trang tự gọi resolver, hiển thị tiêu đề, thumbnail và định dạng MP4 công khai thực sự tìm được. Người dùng bấm Tải video để tải từ CDN.

## URL trang tải

`https://CreatorFlow.fcc-vn.com.vn/public-media/tiktok?url=<URL_ENCODED_TIKTOK_URL>`

Trang tải gọi Web BFF nội bộ `/public-media/tiktok/resolve`, BFF chuyển tiếp đến API `/api/v1/public-media/tiktok/resolve`. Không công khai API 7001, AI 8001 hoặc SQL 1433.

## Tạo Shortcut trên iPhone

1. Mở ứng dụng **Phím tắt (Shortcuts)** → dấu + → đặt tên **CreatorFlow – Tải video**.
2. Mở chi tiết Shortcut → bật **Hiển thị trong Bảng chia sẻ (Show in Share Sheet)**.
3. Ở Accepted Types, chọn **URLs**.
4. Thêm action **Get URLs from Shortcut Input**.
5. Thêm **Get Item from List** → First Item.
6. Thêm **URL Encode** để mã hóa URL nhận được.
7. Thêm action **URL** với địa chỉ `https://CreatorFlow.fcc-vn.com.vn/public-media/tiktok?url=` rồi chèn biến đã mã hóa ở cuối.
8. Thêm **Open URLs**.
9. Lưu Shortcut. Trong TikTok, mở video → Chia sẻ → chọn **CreatorFlow – Tải video**.

## Tải video và lưu vào iPhone

- Trang hiển thị những định dạng MP4 mà metadata công khai xác nhận. Bản hiện tại chỉ có thể hiển thị một URL MP4 nguồn; không tự đặt nhãn HD/SD/không watermark nếu chưa xác minh.
- Nút Tải video mở URL CDN. Safari thường đưa tệp tải về vào **Files → Downloads**. Người dùng có thể mở tệp video rồi dùng Share → Save Video để đưa vào Photos, nếu iOS cho phép với định dạng đó.
- Tải từ trang web không tự động lưu thẳng vào Photos. Muốn tự động Save to Photo Album, cần một Shortcut tải riêng (Get Contents of URL → Save to Photo Album); đây là bước tiếp theo sau khi xác minh luồng tải trên iPhone.

## Kiểm thử và giới hạn

- Thử với video TikTok công khai không yêu cầu đăng nhập.
- Chỉ coi thành công khi tệp tải về có dung lượng hợp lý và phát được.
- Nếu TikTok không cung cấp URL media công khai, trang hiển thị lỗi thay vì tạo link giả.
- URL CDN có thể hết hạn hoặc yêu cầu header/phiên mà trình duyệt không có. Không vượt login, CAPTCHA, DRM hoặc cơ chế kiểm soát truy cập.
- Endpoint không lưu video hoặc URL vào database. Resolver có giới hạn request và allowlist HTTPS cho host TikTok/CDN.

## Giai đoạn 2 (chưa bật)

Sau khi xác nhận tải ổn định, mới bổ sung gửi tệp vào Share Inbox, xác nhận quyền và tạo đúng 3 render jobs.

# CreatorFlow – Tải video TikTok và lưu vào Ảnh (iPhone)

## Hai cách sử dụng

1. **Tải trực tiếp vào Photos bằng Shortcut:** khuyến nghị. Người dùng chọn Chia sẻ → CreatorFlow – Lưu vào Ảnh. Shortcut tự gọi resolver, tải MP4 và chạy Save to Photo Album. Không cần mở Safari.
2. **Trang tải trên web:** mở `https://CreatorFlow.fcc-vn.com.vn/public-media/tiktok?url=<URL_ENCODED_TIKTOK_URL>` để xem tiêu đề, thumbnail và bấm tải thủ công.

Web BFF: `GET /public-media/tiktok/resolve?url=<encoded TikTok URL>`. API nội bộ: `GET /api/v1/public-media/tiktok/resolve`. JSON thành công có trường `availableFormats[0].mediaUrl`. Không công khai API 7001, AI 8001 hoặc SQL 1433.

## Tạo Shortcut một lần trên iPhone

Mở **Phím tắt (Shortcuts)** → dấu **+**, đặt tên **CreatorFlow – Lưu vào Ảnh**.

Trong phần chi tiết Shortcut, bật **Hiển thị trong Bảng chia sẻ (Show in Share Sheet)** và chỉ nhận loại **URLs**.

Thêm các action theo đúng thứ tự sau:

1. **Get URLs from Shortcut Input** – lấy URL video được chia sẻ.
2. **Get Item from List** – chọn **First Item**.
3. **URL Encode** – mã hóa URL TikTok.
4. **URL** – nhập địa chỉ `https://CreatorFlow.fcc-vn.com.vn/public-media/tiktok/resolve?url=` rồi nối kết quả URL Encode vào cuối. Nếu action URL có phần Query, có thể thêm key `url` với giá trị URL TikTok thay vì tự nối chuỗi.
5. **Get Contents of URL** – URL ở bước 4, Method **GET**. Đây là phản hồi JSON của CreatorFlow.
6. **Get Dictionary Value** – lấy key `availableFormats` từ kết quả JSON.
7. **Get Item from List** – chọn **First Item**.
8. **Get Dictionary Value** – lấy key `mediaUrl`.
9. **URL** – chuyển giá trị `mediaUrl` thành URL.
10. **Get Contents of URL** – tải nội dung URL media. Method **GET**. Đầu ra phải là nội dung tệp video, không phải văn bản.
11. **Save to Photo Album** – lưu đầu ra video ở bước 10 vào album **Recents** (hoặc album người dùng chọn nếu iOS cung cấp lựa chọn).
12. **Show Notification** – thông báo `CreatorFlow: Đã lưu video vào Ảnh` sau khi action Save to Photo Album chạy thành công.

Nếu muốn kiểm tra lỗi trước khi tải, chèn **Get Dictionary Value** với key `success` và action **If**: chỉ chạy bước 6–12 khi success là true; nhánh Otherwise hiển thị `Không tìm thấy video MP4 công khai.`. Với phản hồi lỗi HTTP, Shortcut có thể dừng ngay tại Get Contents of URL; đó là hành vi đúng, không nên lưu dữ liệu lỗi như video.

## Sử dụng

1. Mở video TikTok công khai trong ứng dụng TikTok.
2. Chọn **Chia sẻ** → **CreatorFlow – Lưu vào Ảnh**.
3. Chờ Shortcut tải xong. Mở **Ảnh (Photos) → Gần đây (Recents)** để kiểm tra video.
4. Nếu Shortcut không xuất hiện, mở chi tiết Shortcut và xác nhận Show in Share Sheet + Accepted Types = URLs.

## Giới hạn quan trọng

- iOS có thể yêu cầu cấp quyền cho Shortcut thêm ảnh/video vào Photos ở lần đầu.
- Shortcut chỉ tải khi CreatorFlow trả về URL media MP4 công khai hợp lệ. Nếu TikTok không cung cấp media URL, trả lỗi, URL CDN hết hạn hoặc CDN từ chối request, Shortcut sẽ thất bại và không có video để lưu.
- Bản resolver hiện chỉ xác nhận một định dạng MP4 nguồn. Không gắn nhãn HD, SD hoặc không watermark nếu không có dữ liệu chứng minh.
- Một số CDN có thể yêu cầu header hoặc cookie mà Shortcut không có. Không vượt đăng nhập, CAPTCHA, DRM hay cơ chế kiểm soát truy cập.
- Nên thử với một video công khai do chính người dùng đăng hoặc có quyền tải và sử dụng.
- Shortcut này chưa gửi tệp về Share Inbox và chưa tạo render jobs. Đó là giai đoạn tiếp theo sau khi xác nhận tải/lưu Photos hoạt động.

## Kiểm thử đạt

- Chia sẻ video TikTok → Shortcut được gọi với URL đúng.
- Resolver trả JSON thành công, có `availableFormats[0].mediaUrl`.
- Get Contents of URL tải được dữ liệu MP4 có dung lượng hợp lý.
- Save to Photo Album hoàn tất; video xuất hiện trong Photos và phát được.

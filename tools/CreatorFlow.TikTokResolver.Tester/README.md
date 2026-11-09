# CreatorFlow TikTok Resolver Tester

Ứng dụng web độc lập để thử link TikTok công khai mà không cần chạy CreatorFlow API/Web, SQL Server, Keycloak, Share Inbox hoặc render worker.

## Yêu cầu

- .NET SDK 9.0
- Kết nối Internet

## Chạy trên Windows / Z240

Từ thư mục gốc repository:

```powershell
dotnet run --project .\tools\CreatorFlow.TikTokResolver.Tester\CreatorFlow.TikTokResolver.Tester.csproj
```

Mở trình duyệt tại:

`http://127.0.0.1:8095`

Dán link TikTok và nhấn **Phân giải video**. Nếu metadata HTML công khai có URL media trên CDN được cho phép, trang sẽ hiện thông tin video và nút **Tải MP4 về máy tính**.

## API cục bộ

- `GET /health`
- `GET /api/resolve?url=<TikTok URL>`
- `GET /download?mediaUrl=<HTTPS media URL>`

## Giới hạn và an toàn

- Chỉ truy cập HTTPS trên các host TikTok/CDN nằm trong allowlist.
- Chặn redirect ra ngoài host TikTok được cho phép.
- HTML tối đa 5 MB; media tối đa 500 MB.
- Không đăng nhập, không giải CAPTCHA, không vượt DRM hoặc cơ chế kiểm soát truy cập.
- Resolver hiện dùng metadata HTML công khai (`og:video:secure_url`, `og:video`, `twitter:player:stream`). TikTok có thể không cung cấp URL MP4 trực tiếp; kết quả thất bại không có nghĩa video không tồn tại.
- Công cụ này là harness thử nghiệm độc lập, chưa tích hợp Share Inbox, chưa upload video lên CreatorFlow và chưa tạo render jobs.
- Không public cổng 8095 ra Internet. Chỉ bind localhost mặc định.

## Kiểm tra thực tế

Một lần phân giải chỉ được xem là thành công khi:
1. API trả `success: true` và media URL HTTPS hợp lệ.
2. Nút tải trả dữ liệu `video/mp4` hoặc octet-stream từ host CDN được cho phép.
3. Tệp tải về có dung lượng hợp lý và phát được trong trình phát video.

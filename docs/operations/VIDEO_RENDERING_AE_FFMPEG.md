# Dựng video tự động bằng After Effects và FFmpeg

## Luồng đã được tích hợp
1. Người dùng tải MP4/MOV/M4V lên Thư viện video (tối đa 500 MB) và xác nhận quyền sử dụng.
2. API lưu file ngoài SQL Server, ghi metadata, SHA-256 và tenant sở hữu.
3. API tạo tác vụ bền vững trong SQL Server.
4. Worker Windows trên HP Z240 kiểm tra video nguồn bằng FFprobe (1 giây–10 phút), dựng composition dọc 1080×1920 bằng After Effects, xuất chuỗi PNG `frame_[#####].png` bằng aerender, sau đó FFmpeg đọc chuỗi bằng mẫu `frame_%05d.png`, ghép âm thanh nguồn và mã hóa MP4 H.264/AAC theo thời lượng video nguồn.
5. Trang Thư viện video tự cập nhật tiến độ; video hoàn tất được phát trong CreatorFlow và tải MP4.
6. API kiểm tra tenant khi xem trạng thái, phát hoặc tải video; phát hỗ trợ HTTP Range.

## Phạm vi bộ dựng ban đầu
Bản đầu crop video nguồn để lấp đầy khung dọc, thêm tiêu đề, câu mở đầu và lời kêu gọi hành động. Chưa có thư viện hiệu ứng cao cấp, B-roll AI, phụ đề tự nhận dạng, giọng đọc AI hoặc đồng bộ nhạc theo nhịp. Những nội dung này cần template và dịch vụ AI tiếp theo; không được coi là đã hoàn thành trong bản đầu.

## Cấu hình bắt buộc trên HP Z240
API và Worker phải dùng cùng một thư mục VideoRendering:StorageRoot trên máy chủ Windows. Worker cần ConnectionStrings:CreatorFlow, VideoRendering:AfterEffectsExe, VideoRendering:AerenderExe, VideoRendering:FFmpegExe và VideoRendering:FFprobeExe (bắt buộc; có thể tự dò nếu đặt cạnh FFmpeg).

Ví dụ đường dẫn (điều chỉnh theo phiên bản Adobe đã cài):
- C:\\Program Files\\Adobe\\Adobe After Effects 2026\\Support Files\\AfterFX.exe
- C:\\Program Files\\Adobe\\Adobe After Effects 2026\\Support Files\\aerender.exe

Không đưa connection string, API key hoặc thông tin giấy phép vào Git. Worker cần quyền đọc/ghi thư mục video và quyền SQL tối thiểu trên hai bảng VideoAssets, VideoRenderJobs. Chạy worker dưới tài khoản Windows có quyền truy cập SQL; không mở cổng mạng cho worker.

## Database
Migration 017 tạo bảng tài sản/video job. Migration 018 bổ sung khóa ngoại ghép TenantId để SQL Server không cho job tham chiếu video nguồn của workspace khác và inbox không thể liên kết kênh của workspace khác. Migration 019 bổ sung khóa ngoại cùng tenant cho các quan hệ dự án, thử nghiệm phong cách, phản hồi và dữ liệu xu hướng. Migration 020 cho phép cùng một kênh/video/hồ sơ tồn tại độc lập trong nhiều workspace. Cả hai migration được đưa vào database/Apply-Migrations.ps1. Chạy sau khi xác nhận đúng database CreatorFlow_QC:
pwsh -File database/Apply-Migrations.ps1 -SqlInstance localhost -Database CreatorFlow_QC

## Chạy worker
Từ thư mục gốc repo: dotnet run --project src/CreatorFlow.Workers/CreatorFlow.Workers.csproj
Worker xử lý tuần tự, có timeout cho từng tiến trình và giới hạn tối đa ba lần thử. Nên nghiệm thu chạy foreground trước khi đăng ký Windows Service.

## API
- POST /api/v1/video/assets: multipart form gồm file, confirmRights=true, tùy chọn rightsNote.
- GET /api/v1/video/assets: danh sách video nguồn trong workspace.
- POST /api/v1/video/render-jobs: tạo một phương án đơn lẻ. JSON gồm assetId, title, hook, callToAction, styleKey.
- POST /api/v1/video/render-jobs/batch: tạo nguyên tử 2–3 phương án khác nhau; nếu một lần ghi thất bại, toàn bộ nhóm được rollback. JSON gồm assetId, title, hook, callToAction, styleKeys.
- GET /api/v1/video/render-jobs: lịch sử 100 tác vụ gần nhất.
- GET /api/v1/video/render-jobs/{id}: trạng thái.
- GET /api/v1/video/render-jobs/{id}/preview: stream MP4.
- GET /api/v1/video/render-jobs/{id}/download: tải MP4.

## Nghiệm thu trên máy thật
1. Kiểm tra After Effects có license, font Arial và mở được; kiểm tra đường dẫn aerender.
2. Chạy scripts/Test-VideoRenderer.ps1 với bốn đường dẫn công cụ.
3. Dựng video 15–30 giây có âm thanh; kiểm tra crop, chữ tiếng Việt, tiếng, thời lượng và phát trên iPhone.
4. Tải lên cùng file hai lần; API phải trả về cùng asset theo SHA-256 trong workspace, không tạo bản sao.
5. Tạo hai tác vụ; xác minh worker không nhận trùng job.
6. Kiểm tra tài khoản tenant khác không xem/tải được video.
7. Đo thời gian dựng, CPU/RAM và dung lượng. Z240 nên chạy một tác vụ tại một thời điểm.

CI build không thay thế nghiệm thu After Effects/FFmpeg thật trên HP Z240. Không tuyên bố sẵn sàng sản xuất trước khi kiểm tra phần cứng, quyền truy cập file và tenant isolation.

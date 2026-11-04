# Dựng video tự động bằng FFmpeg (renderer hiện tại)

## Phạm vi

Renderer local hiện tại của CreatorFlow là **FFmpeg-only**. After Effects không phải dependency và không được gọi trong luồng render này; có thể bổ sung adapter After Effects trong tương lai nhưng không nằm trong nghiệm thu hiện tại.

Worker chạy như tiến trình Windows riêng trên HP Z240, ngoài Docker Web/AI. API tạo durable render jobs trong SQL Server; worker nhận job, kiểm tra đầu vào, gọi FFprobe/FFmpeg, cập nhật trạng thái và lưu MP4 trong vùng lưu trữ riêng ngoài web root. API cung cấp trạng thái, preview và download có xác thực.

## Thành phần

- FFmpeg: encode video dọc H.264/AAC MP4.
- FFprobe: đọc metadata và xác minh video stream/duration.
- SQL Server: lưu asset metadata, render jobs và trạng thái.
- Storage root: thư mục Windows riêng, dùng chung cấu hình giữa API và worker.
- Worker: `scripts/Start-VideoRenderWorker.ps1`.

## Cấu hình trên Windows

Đặt các biến `VideoRendering__*` trong `.env` theo `.env.example`, tối thiểu xác định đường dẫn FFmpeg, FFprobe và storage root. Trên máy Z240 hiện tại, FFmpeg đã được cài tại `C:\Tools\ffmpeg\bin\ffmpeg.exe`; kiểm tra lại đường dẫn thực tế trước khi chạy. API và worker phải dùng cùng một `VideoRendering__StorageRoot`.

Không đưa `.env`, khóa API hoặc dữ liệu người dùng vào Git. Worker cần chạy dưới tài khoản Windows có quyền truy cập SQL Server và thư mục storage.

## Cơ sở dữ liệu

Migrations 017–020 tạo nền tảng asset/render job, quan hệ cùng tenant và ràng buộc workspace-scoped. Chạy migration runner theo hướng dẫn Z240 và xác nhận ledger/checksum trước khi khởi động worker. Không sửa migration đã áp dụng; nếu cần thay đổi schema, tạo migration mới.

## Khởi động và nghiệm thu

```powershell
. .\scripts\docker\Import-LocalPilotEnv.ps1
Import-LocalPilotEnv
.\scripts\Test-VideoRenderer.ps1
.\scripts\Start-VideoRenderWorker.ps1
```

Sau đó mở Thư viện video, tải lên một video mà workspace có quyền sử dụng, tạo render job, chờ hoàn tất, xem preview và tải MP4. Nghiệm thu tối thiểu một video dọc 15–30 giây: file phải tồn tại, FFprobe đọc được stream/duration, phát được và có âm thanh đúng kỳ vọng. Kiểm tra log worker nếu job thất bại; không coi trạng thái API/HTTP 200 là bằng chứng render thành công.

## Giới hạn hiện tại

- Không có STT/TTS, phân tích nhạc, beat-sync, B-roll hay hiệu ứng nâng cao.
- Không tự động đăng tải lên TikTok hoặc nền tảng khác.
- Không tự động xóa watermark hoặc xử lý nhằm né chính sách nền tảng.
- Render job queue không đồng nghĩa với connector discovery queue.
- CI build/test không thay thế nghiệm thu FFmpeg thật trên HP Z240.

Style Lab có thể tạo gói handoff cho quy trình biên tập ngoài hệ thống; gói handoff hoặc composition After Effects tương lai không làm thay đổi engine render FFmpeg-only hiện tại.

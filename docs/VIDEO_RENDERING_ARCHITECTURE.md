# Kiến trúc dựng video CreatorFlow

## Mục tiêu

CreatorFlow dùng hợp đồng `IVideoRenderEngine` để tách quy trình nghiệp vụ khỏi công cụ dựng video. Hàng đợi SQL, kiểm soát tenant, lưu trữ, trạng thái tác vụ và lịch sử MP4 không phụ thuộc vào After Effects hay FFmpeg.

## Engine hiện tại: FFmpeg

- Engine mặc định: `VideoRendering:Engine=ffmpeg`.
- Bắt buộc: `ffmpeg.exe`, `ffprobe.exe`, font TrueType hỗ trợ tiếng Việt.
- Không cần cài Adobe After Effects.
- Xuất MP4 H.264/AAC, 1080×1920, giữ âm thanh nguồn nếu có.
- Có thể chèn tiêu đề, hook và CTA bằng chữ tiếng Việt với hiệu ứng hiện dần đơn giản.
- Các kiểu bố cục hiện được chọn qua `StyleKey`; đây là bộ preset cơ bản, chưa phải thư viện motion graphics nâng cao.

## Engine tương lai

Khi cần After Effects, thêm một implementation khác của `IVideoRenderEngine` và đăng ký qua cấu hình/DI. Không thay đổi SQL job queue, API tạo tác vụ, kiểm tra tenant, lưu trữ hoặc UI xem/tải MP4. Không bật engine chưa được cài đặt và kiểm thử.

## Pipeline xử lý nội dung

Pipeline dự kiến tách thành các bước có thể bật/tắt độc lập:

1. Phân tích video và xác định nội dung được phép sử dụng.
2. Tiền xử lý hình ảnh/phụ đề (OCR và xử lý vùng chữ là module riêng).
3. Tạo nội dung bình luận, hook, phụ đề và CTA.
4. Chọn preset phong cách.
5. Render MP4 qua engine đã chọn.
6. FFprobe xác minh đầu ra, tính SHA-256 và cập nhật trạng thái.

**Giới hạn hiện tại:** OCR nhận diện và xóa chữ Trung Quốc chưa được triển khai trong thay đổi engine này. Nếu chữ nằm trong hình ảnh, việc xóa cần OCR + inpainting/masking; FFmpeg chỉ thực hiện các bộ lọc hình ảnh/che phủ sau khi đã có vùng tọa độ. Phụ đề dạng track riêng có thể được loại bỏ ở bước remux khi có dữ liệu track phù hợp.

## Cấu hình Windows

```dotenv
VideoRendering__Engine=ffmpeg
VideoRendering__StorageRoot=C:/CreatorFlowData/Video
VideoRendering__FFmpegExe=C:/Tools/ffmpeg/bin/ffmpeg.exe
VideoRendering__FFprobeExe=C:/Tools/ffmpeg/bin/ffprobe.exe
VideoRendering__FontFile=C:/Windows/Fonts/arial.ttf
```

Giữ cùng `StorageRoot` cho API và worker. Không đưa file nguồn, MP4, key hoặc thông tin đăng nhập vào Git.

## Kiểm thử chấp nhận

- Build .NET và test suite trên đúng commit.
- Render video thử có âm thanh và video không có âm thanh.
- Kiểm tra tiếng Việt có dấu, dấu câu, đường dẫn có khoảng trắng.
- Xác minh MP4 bằng FFprobe: 1080×1920, duration hợp lệ, audio được giữ nếu nguồn có.
- Kiểm tra 2–3 StyleKey tạo được MP4 độc lập.
- Kiểm tra tenant isolation và đường dẫn file trước khi mở Go-live.

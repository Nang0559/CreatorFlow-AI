# Chạy kiểm tra CreatorFlow trên HP Z240

## Mục đích

Script `scripts/Invoke-CreatorFlow-Z240-Validation.ps1` gom các bước kiểm tra môi trường Z240: Git branch, .NET restore/build/test, Python AI tests, Docker Compose config/build, trạng thái container, HTTP health và đọc migration ledger SQL.

Script **không**:
- In hoặc gửi nội dung `.env`, API key hay connection string.
- Tự áp dụng SQL migration hoặc sửa dữ liệu.
- Tự khởi động API native Windows nếu API chưa chạy.
- Tuyên bố Go-live chỉ vì health endpoint trả HTTP 200.

## Chuẩn bị một lần

1. Mở PowerShell 7 (không dùng Windows PowerShell 5.1).
2. Mở thư mục dự án:
   ```powershell
   Set-Location E:\Projects\CreatorFlow-AI
   ```
3. Đồng bộ nhánh:
   ```powershell
   git fetch origin
   git switch feature/trend-selection-safety-intelligence
   git pull --ff-only origin feature/trend-selection-safety-intelligence
   ```
4. Đảm bảo file `.env` hiện có trên Z240. Không gửi file này lên chat/GitHub.
5. Đảm bảo API native Windows đang chạy ở `http://localhost:7001`. Web và AI chạy bằng Compose theo cấu hình hiện tại.

## Chạy toàn bộ kiểm tra

Nếu working tree sạch và các dịch vụ đã được khởi động:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\Invoke-CreatorFlow-Z240-Validation.ps1
```

Nếu muốn script tự chạy/recreate các dịch vụ Compose trước khi kiểm tra:

```powershell
.\scripts\Invoke-CreatorFlow-Z240-Validation.ps1 -StartCompose
```

Nếu cần kiểm tra lại khi có thay đổi local chưa commit:

```powershell
.\scripts\Invoke-CreatorFlow-Z240-Validation.ps1 -AllowDirtyWorkingTree
```

## Kết quả cần gửi lại

Script ghi báo cáo vào:

`artifacts\z240-validation\z240-validation-<thời-điểm>.txt`

Gửi nội dung báo cáo này cùng:
- Ảnh trang `http://localhost:8080` sau khi đăng nhập.
- DevTools Console nếu có lỗi.
- Network → Fetch/XHR: request lỗi đầu tiên (URL, HTTP status, response body đã che token/cookie).
- Nếu SQL bị lỗi: thông báo lỗi và kết quả kiểm tra migration ledger; không gửi connection string.

Không gửi `.env`, access token, cookie, mật khẩu, mã OTP hoặc file log có chứa bí mật.

## Cổng SQL

Migration 021 tạo bảng lịch đồng bộ kênh và hàng đợi tải video có kiểm tra quyền sử dụng. Script chỉ đọc ledger. Nếu migration 021 chưa có trong ledger, **không tự chạy migration**; cần kiểm tra trạng thái migration 011–013 và các migration 016–020 trên chính database QC trước. Không áp dụng tenant migration lên database đang có dữ liệu nếu chưa xác nhận cách gán quyền sở hữu workspace.

## Diễn giải

- `PASS`: bước kiểm tra đó chạy thành công.
- `FAIL`: có lỗi cần xử lý trước khi tiếp tục.
- `BLOCKED`: thiếu điều kiện hoặc migration chưa được áp dụng; không đồng nghĩa ứng dụng đã hỏng.
- Health HTTP thành công chỉ xác nhận tiến trình phản hồi, không xác nhận nghiệp vụ end-to-end.

Khi script hoàn tất, vẫn cần kiểm thử thao tác thực tế trên UI: tạo chủ đề, đăng ký/theo dõi kênh, gửi link vào hộp thư, tạo nội dung/biến thể, tải video có quyền sử dụng, dựng MP4, xem trước và tải MP4. Chức năng quét Douyin tự động vẫn phụ thuộc connector chính thức/được cấp phép và worker tương ứng; migration 021 một mình không bật được tính năng đó.

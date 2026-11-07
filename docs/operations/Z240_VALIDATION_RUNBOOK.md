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
   git switch feature/keycloak-oidc-local
   git pull --ff-only origin feature/keycloak-oidc-local
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

Migration 021 tạo bảng lịch đồng bộ kênh và hàng đợi tải video có kiểm tra quyền sử dụng. Các migration 025–027 bổ sung lịch khám phá theo chủ đề; migration 028 lưu cấu hình khóa connector theo workspace. Script chỉ đọc ledger. Nếu migration 028 chưa có trong ledger, **không tự suy luận schema đã sẵn sàng**; cần kiểm tra trạng thái migration 011–013 và toàn bộ migration tenant 016–028 trên chính database QC trước. Migration 028 phụ thuộc bảng Tenants từ migration 011–013. Không áp dụng tenant migration lên database đang có dữ liệu nếu chưa xác nhận cách gán quyền sở hữu workspace.

## Diễn giải

- `PASS`: bước kiểm tra đó chạy thành công.
- `FAIL`: có lỗi cần xử lý trước khi tiếp tục.
- `BLOCKED`: thiếu điều kiện hoặc migration chưa được áp dụng; không đồng nghĩa ứng dụng đã hỏng.
- Health HTTP thành công chỉ xác nhận tiến trình phản hồi, không xác nhận nghiệp vụ end-to-end.

Khi script hoàn tất, vẫn cần kiểm thử thao tác thực tế trên UI: tạo chủ đề, đăng ký/theo dõi kênh, gửi link vào hộp thư, tạo nội dung/biến thể, tải video có quyền sử dụng, dựng MP4, xem trước và tải MP4. Connector Brave/TikTok Research hiện chỉ chạy theo yêu cầu từ trang Xu hướng; chưa tự chạy theo lịch, chưa lưu kết quả thành candidate và không tải media. TikTok Research API cần phê duyệt riêng. Không có tự động quét Douyin qua QR/session.

## SQL tenant migrations — chỉ thực hiện sau khi có xác nhận

Lệnh migration thông thường không bật tenant migrations. Sau khi đã sao lưu database QC và xác nhận toàn bộ dữ liệu hiện tại thuộc duy nhất một workspace, lệnh đầy đủ sẽ là:

```powershell
.\database\Apply-Migrations.ps1 `
    -SqlInstance localhost `
    -Database CreatorFlow_QC `
    -IncludeTenantMigrations `
    -ConfirmLegacyWorkspaceAssignment
```

Lệnh này bao gồm các migration tenant 011–013 và các migration phụ thuộc 016–028. Migration 012 gán các bản ghi hiện hữu trong danh sách bảng của nó cho workspace `legacy-workspace`. Nếu database có dữ liệu của nhiều khách hàng hoặc chưa rõ chủ sở hữu, **không chạy lệnh này**; cần lập mapping TenantId theo dữ liệu đã được duyệt trước.

Sau khi migration chạy, chạy lại script kiểm tra để xác nhận ledger ghi nhận 021–028 theo đúng các script hiện có trên nhánh. Không áp dụng lại hoặc chỉnh sửa nội dung migration đã ghi nhận checksum. Migration 022 hiện chỉ tạo cấu trúc gói/quota/usage; chưa bật thanh toán hoặc thực thi quota.

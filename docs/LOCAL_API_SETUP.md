# Chạy CreatorFlow API trên máy local

API tự nạp các biến trong file `.env` ở thư mục gốc repository khi khởi động. Bạn không cần chạy script PowerShell để import biến môi trường trước mỗi lần chạy.

Từ PowerShell tại thư mục repository:

```powershell
cd E:\Projects\CreatorFlow-AI
dotnet run --no-build --project .\src\CreatorFlow.Api\CreatorFlow.Api.csproj --no-launch-profile
```

Nếu cần build mã nguồn mới trước khi chạy:

```powershell
dotnet build .\src\CreatorFlow.Api\CreatorFlow.Api.csproj
dotnet run --no-build --project .\src\CreatorFlow.Api\CreatorFlow.Api.csproj --no-launch-profile
```

Khi tìm thấy `.env`, API chỉ ghi số lượng biến đã nạp; không ghi giá trị bí mật. Các biến đã được thiết lập trong Windows/PowerShell/Docker sẽ được ưu tiên hơn giá trị trong `.env`.

## Tùy chọn

- Tắt tự nạp `.env` cho một lần chạy: `$env:CREATORFLOW_LOAD_DOTENV='false'`
- Chỉ định file cấu hình khác: `$env:CREATORFLOW_DOTENV_PATH='D:\CreatorFlow\api.local.env'`
- Xóa hai biến tùy chọn trên khỏi PowerShell sau khi dùng nếu muốn quay lại mặc định.

Không commit file `.env` và không gửi API key/client secret vào log hoặc chat.

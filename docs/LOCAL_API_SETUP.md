# Chạy CreatorFlow API trên máy local

Local `.env` bootstrap hiện là **opt-in**. Mặc định API và test không tự tìm/nạp file `.env`, tránh vô tình đưa bí mật của máy phát triển vào test hoặc môi trường dịch vụ.

Từ PowerShell tại thư mục repository:

```powershell
cd E:\Projects\CreatorFlow-AI
$env:CREATORFLOW_LOAD_DOTENV = 'true'
dotnet run --no-build --project .\src\CreatorFlow.Api\CreatorFlow.Api.csproj --no-launch-profile
```

Nếu cần build mã nguồn mới trước khi chạy:

```powershell
dotnet build .\src\CreatorFlow.Api\CreatorFlow.Api.csproj
$env:CREATORFLOW_LOAD_DOTENV = 'true'
dotnet run --no-build --project .\src\CreatorFlow.Api\CreatorFlow.Api.csproj --no-launch-profile
```

Tắt bootstrap cho tiến trình hiện tại bằng:

```powershell
Remove-Item Env:CREATORFLOW_LOAD_DOTENV -ErrorAction SilentlyContinue
```

Có thể chỉ định file cấu hình khác bằng `CREATORFLOW_DOTENV_PATH`. Biến môi trường đã tồn tại luôn được ưu tiên hơn file. Không commit `.env`, không ghi API key/client secret ra log hoặc gửi vào chat.

## Custom discovery providers

Workspace provider hỗ trợ HTTP GET hoặc POST + JSON. Endpoint tenant cấu hình phải là **HTTPS công khai trên cổng 443**. API sử dụng transport riêng: DNS được phân giải tại thời điểm kết nối; mọi địa chỉ trả về phải là địa chỉ unicast công khai; kết nối tới loopback, private, link-local, shared, multicast và dải reserved bị từ chối. Redirect bị chặn. Response bị giới hạn 2 MiB và thời gian chờ bị giới hạn. Proxy hệ thống không được sử dụng.

Không hỗ trợ endpoint HTTP, localhost, IP nội bộ, self-hosted SearXNG trong LAN, hoặc dịch vụ trong Docker. Đây là giới hạn an toàn có chủ đích, không phải lỗi cấu hình. Nếu cần tích hợp dịch vụ nội bộ, hãy xây dựng adapter do hệ thống quản trị/allowlist kiểm soát, không mở rộng endpoint tenant tùy ý.

Các header bổ sung và API key được lưu trong blob cấu hình đã mã hóa bằng ASP.NET Core Data Protection. GET không trả giá trị header hoặc API key; chỉ trả cờ cho biết bí mật đã được lưu. Khi sửa, để trống trường bí mật để giữ giá trị cũ. Tên nguồn tùy chỉnh được hiển thị với tiền tố `Custom:`; không được dùng nhãn dành riêng cho Brave Search hoặc TikTok Research API.

Các provider POST cần JSON body hợp lệ. Ví dụ LangSearch:

```json
{
  "query": "{query}",
  "count": 10
}
```

JSON path kết quả phải trỏ tới mảng; chỉ URL TikTok HTTPS hợp lệ mới được giữ làm candidate đầu vào. Custom provider hiện vẫn là truy vấn tức thời, chưa tự lưu candidate bền vững hoặc tự chạy lịch.

Không cần migration SQL cho thay đổi transport và cách trả metadata bí mật trong đợt này.

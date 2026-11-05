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


## Khai báo thêm nhà cung cấp tìm kiếm (không cần sửa code)

API hỗ trợ thêm các dịch vụ tìm kiếm tương thích **HTTP GET + JSON** bằng biến cấu hình `DiscoverySearch:Providers`. Mỗi phần tử có tên, endpoint, tên query parameter và đường dẫn JSON tới mảng kết quả/URL/tiêu đề/nội dung. Chỉ kết quả URL TikTok HTTPS hợp lệ mới được đưa vào pipeline Trend Intelligence.

Ví dụ khai báo một SearXNG tự host (không cần API key) trong file `.env`:

```dotenv
DiscoverySearch__Providers__0__Enabled=true
DiscoverySearch__Providers__0__Name=SearXNG
DiscoverySearch__Providers__0__Endpoint=http://localhost:8888/search?format=json
DiscoverySearch__Providers__0__QueryParameter=q
DiscoverySearch__Providers__0__QueryTemplate=site:tiktok.com {topic} {query}
DiscoverySearch__Providers__0__ResultsPath=results
DiscoverySearch__Providers__0__UrlPath=url
DiscoverySearch__Providers__0__TitlePath=title
DiscoverySearch__Providers__0__DescriptionPath=content
DiscoverySearch__Providers__0__MaxQueries=4
DiscoverySearch__Providers__0__TimeoutSeconds=15
```

Ví dụ provider có API key:

```dotenv
DiscoverySearch__Providers__1__Enabled=true
DiscoverySearch__Providers__1__Name=MySearch
DiscoverySearch__Providers__1__Endpoint=https://search.example.com/api/search
DiscoverySearch__Providers__1__QueryParameter=query
DiscoverySearch__Providers__1__QueryTemplate={topic} {query}
DiscoverySearch__Providers__1__ResultsPath=data.results
DiscoverySearch__Providers__1__UrlPath=url
DiscoverySearch__Providers__1__TitlePath=title
DiscoverySearch__Providers__1__DescriptionPath=snippet
DiscoverySearch__Providers__1__ApiKey=replace-with-your-key
DiscoverySearch__Providers__1__ApiKeyHeader=Authorization
DiscoverySearch__Providers__1__ApiKeyPrefix="Bearer "
```

Các trường `ResultsPath`, `UrlPath`, `TitlePath`, `DescriptionPath` là đường dẫn thuộc tính JSON phân tách bằng dấu chấm. `ApiKey`, `ApiKeyHeader`, `ApiKeyPrefix` là tùy chọn. Có thể khai báo nhiều provider bằng chỉ số `0`, `1`, `2`... Tên provider nên duy nhất để dễ theo dõi trạng thái.

Đây là adapter tìm kiếm JSON GET tổng quát, không phải bộ chuyển đổi mọi API bất kỳ. Provider chỉ hỗ trợ POST, OAuth đặc thù, CAPTCHA hoặc schema không tương thích cần adapter riêng. Không khai báo endpoint không đáng tin cậy; chỉ quản trị viên máy chủ nên chỉnh cấu hình này. API không ghi API key ra log.

Sau khi sửa `.env`, khởi động lại API. Không cần migration SQL. Trang **Nguồn tìm kiếm → Kiểm tra tất cả nguồn** sẽ hiển thị trạng thái provider đã khai báo cùng Brave/TikTok.

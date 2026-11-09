# CreatorFlow TikTok Resolver Tester

Ứng dụng web độc lập để thử link TikTok công khai mà không cần chạy CreatorFlow API/Web, SQL Server, Keycloak, Share Inbox hoặc render worker.

## Yêu cầu

- .NET SDK 9.0
- Python 3 (khuyến nghị) hoặc bản yt-dlp executable
- Kết nối Internet
- FFmpeg chỉ cần khi yt-dlp phải ghép các luồng audio/video riêng; không bắt buộc với MP4 đơn luồng.

## Cài đặt trên Windows / Z240

Mở PowerShell 7:

\`\`\`powershell
py -m pip install --upgrade yt-dlp
yt-dlp --version
dotnet --version
\`\`\`

Nếu lệnh \`yt-dlp\` không được tìm thấy, có thể đặt đường dẫn executable rõ ràng:

\`\`\`powershell
$env:CREATORFLOW_YTDLP_PATH = "C:\\Tools\\yt-dlp\\yt-dlp.exe"
\`\`\`

Nếu cài bằng pip nhưng Windows không đưa thư mục Scripts vào PATH, dùng đường dẫn \`yt-dlp.exe\` trong thư mục Scripts của Python hoặc cài bản executable độc lập. Không cần cài package yt-dlp vào project .NET.

## Chạy

Từ thư mục gốc repository:

\`\`\`powershell
git pull origin feature/public-tiktok-share-resolver
dotnet build .\\tools\\CreatorFlow.TikTokResolver.Tester\\CreatorFlow.TikTokResolver.Tester.csproj
dotnet run --project .\\tools\\CreatorFlow.TikTokResolver.Tester\\CreatorFlow.TikTokResolver.Tester.csproj
\`\`\`

Mở trình duyệt tại:

\`http://127.0.0.1:8095\`

Dán link TikTok và nhấn **Phân giải video**. Công cụ thử lần lượt:
1. Redirect TikTok và metadata HTML / OpenGraph.
2. JSON nhúng công khai như \`__UNIVERSAL_DATA_FOR_REHYDRATION__\` và \`SIGI_STATE\`.
3. yt-dlp TikTok extractor (nếu yt-dlp đã cài và nằm trong PATH hoặc được khai báo qua \`CREATORFLOW_YTDLP_PATH\`).

Nếu phân giải thành công, nút **Tải MP4 về máy tính** gọi yt-dlp với URL TikTok gốc, lưu tạm, kiểm tra chữ ký MP4 rồi mới gửi tệp về trình duyệt. Tệp tạm được xóa sau mỗi lần tải.

## API cục bộ

- \`GET /health\` — trạng thái dịch vụ và yt-dlp.
- \`GET /api/resolve?url=<TikTok URL>\` — phân giải và trả thông tin chẩn đoán.
- \`GET /download?url=<TikTok URL>\` — tải bằng yt-dlp, kiểm tra MP4.
- \`GET /download?mediaUrl=<HTTPS media URL>\` — tải trực tiếp từ CDN allowlist, theo redirect có kiểm tra host và xác minh chữ ký MP4.

## Bảo mật và giới hạn

- URL đầu vào chỉ nhận HTTPS từ host TikTok/TikTok short-link được cho phép.
- Redirect trang TikTok và CDN được kiểm tra từng bước; không cho phép redirect ra host ngoài allowlist.
- yt-dlp chạy không cookie, không playlist, không dùng cache; không yêu cầu thông tin đăng nhập.
- HTML tối đa 5 MB; video tối đa 500 MB; timeout phân giải/tải có giới hạn.
- Không đăng nhập, không giải CAPTCHA, không vượt DRM hoặc cơ chế kiểm soát truy cập.
- Không phải video công khai nào cũng có thể tải trực tiếp. TikTok có thể chặn truy cập, thay đổi extractor hoặc chỉ cung cấp luồng không phù hợp.
- Trang lỗi hiển thị JSON chẩn đoán; không cần mở DevTools để lấy thông tin cơ bản.
- Chỉ bind localhost mặc định tại \`127.0.0.1:8095\`. Không public cổng thử nghiệm này trực tiếp ra Internet.

## Kiểm tra nghiệm thu

Chỉ xác nhận thành công khi:
1. API trả \`success: true\`.
2. Nút tải hoàn tất và trả tệp MP4.
3. Tệp có chữ ký \`ftyp\`, dung lượng hợp lý và phát được trong trình phát video.

Project này chỉ là harness kiểm thử độc lập; chưa tích hợp Share Inbox, chưa upload video lên CreatorFlow và chưa tạo 3 render jobs.

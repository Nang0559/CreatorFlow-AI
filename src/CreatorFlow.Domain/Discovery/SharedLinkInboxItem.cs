using System.Security.Cryptography;
using System.Text;

namespace CreatorFlow.Domain.Discovery;

public enum SharedLinkStatus { Pending, Reviewed, Linked, Ignored }

public sealed class SharedLinkInboxItem
{
    private SharedLinkInboxItem() { }

    public Guid Id { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string SharedUrl { get; private set; } = string.Empty;
    public string UrlHash { get; private set; } = string.Empty;
    public DateTimeOffset SharedAt { get; private set; }
    public SharedLinkStatus Status { get; private set; }
    public string? ReviewNote { get; private set; }
    public Guid? LinkedChannelId { get; private set; }

    public static SharedLinkInboxItem Create(string sharedUrl, DateTimeOffset now)
    {
        if (!Uri.TryCreate(sharedUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !IsSupportedHost(uri.Host))
            throw new ArgumentException("Chỉ chấp nhận liên kết HTTPS hợp lệ từ Douyin, TikTok, YouTube, Facebook hoặc Instagram; không chấp nhận cổng hoặc thông tin đăng nhập nhúng.", nameof(sharedUrl));

        var safeUrl = uri.GetLeftPart(UriPartial.Path) + uri.Query;
        if (safeUrl.Length > 1200)
            throw new ArgumentException("Liên kết quá dài.", nameof(sharedUrl));

        var platform = uri.Host.EndsWith("douyin.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.EndsWith("iesdouyin.com", StringComparison.OrdinalIgnoreCase) ? "Douyin" :
                       uri.Host.EndsWith("tiktok.com", StringComparison.OrdinalIgnoreCase) ? "TikTok" :
                       uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ? "YouTube" :
                       uri.Host.EndsWith("facebook.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.Equals("fb.watch", StringComparison.OrdinalIgnoreCase) ? "Facebook" : "Instagram";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.Scheme.ToLowerInvariant() + "://" + uri.IdnHost.ToLowerInvariant() + uri.PathAndQuery)));
        return new SharedLinkInboxItem
        {
            Id = Guid.NewGuid(), Platform = platform, SharedUrl = safeUrl,
            UrlHash = hash, SharedAt = now, Status = SharedLinkStatus.Pending
        };
    }

    public void MarkReviewed(string? note)
    {
        if (Status is SharedLinkStatus.Linked or SharedLinkStatus.Ignored)
            throw new InvalidOperationException("Liên kết đã được xử lý, không thể chuyển về trạng thái xem xét.");
        if (note?.Length > 500) throw new ArgumentException("Ghi chú tối đa 500 ký tự.", nameof(note));
        Status = SharedLinkStatus.Reviewed;
        ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public void LinkChannel(Guid channelId)
    {
        if (channelId == Guid.Empty) throw new ArgumentException("Channel ID không hợp lệ.", nameof(channelId));
        if (Platform != "Douyin") throw new InvalidOperationException("Chỉ liên kết chia sẻ Douyin mới có thể gắn với kênh Douyin.");
        if (Status == SharedLinkStatus.Ignored) throw new InvalidOperationException("Liên kết đã bị bỏ qua.");
        LinkedChannelId = channelId;
        Status = SharedLinkStatus.Linked;
    }

    public void Ignore(string? note)
    {
        if (note?.Length > 500) throw new ArgumentException("Ghi chú tối đa 500 ký tự.", nameof(note));
        if (Status == SharedLinkStatus.Linked) throw new InvalidOperationException("Liên kết đã gắn với kênh.");
        Status = SharedLinkStatus.Ignored;
        ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private static bool IsSupportedHost(string host)
    {
        var value = host.TrimEnd('.').ToLowerInvariant();
        return value == "douyin.com" || value.EndsWith(".douyin.com", StringComparison.Ordinal) ||
               value == "iesdouyin.com" || value.EndsWith(".iesdouyin.com", StringComparison.Ordinal) ||
               value == "tiktok.com" || value.EndsWith(".tiktok.com", StringComparison.Ordinal) ||
               value == "youtube.com" || value.EndsWith(".youtube.com", StringComparison.Ordinal) ||
               value == "youtu.be" || value == "facebook.com" || value.EndsWith(".facebook.com", StringComparison.Ordinal) ||
               value == "fb.watch" || value == "instagram.com" || value.EndsWith(".instagram.com", StringComparison.Ordinal);
    }
}

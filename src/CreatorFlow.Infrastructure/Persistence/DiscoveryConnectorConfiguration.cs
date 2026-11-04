namespace CreatorFlow.Infrastructure.Persistence;

/// <summary>Tenant-owned discovery provider credentials. Values are always DataProtection ciphertext.</summary>
public sealed class DiscoveryConnectorConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string? EncryptedBraveApiKey { get; set; }
    public string? EncryptedTikTokResearchClientKey { get; set; }
    public string? EncryptedTikTokResearchClientSecret { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

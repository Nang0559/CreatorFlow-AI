namespace CreatorFlow.Infrastructure.Persistence;

/// <summary>
/// Tenant-owned provider configuration. EncryptedApiKey must contain ciphertext produced by
/// the API's configured secret-protection service; it must never contain a plaintext credential.
/// </summary>
public sealed class AIProviderConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "openai-compatible";
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? EncryptedApiKey { get; set; }
    public bool IsActive { get; set; } = true;
    public string RoutingRole { get; set; } = "Fallback";
    public int? FallbackOrder { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxOutputTokens { get; set; } = 3000;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}

using CreatorFlow.Infrastructure.Persistence;

namespace CreatorFlow.Api.Identity;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public string? Role { get; private set; }
    public bool IsResolved => TenantId.HasValue && TenantId.Value != Guid.Empty;

    public void Set(Guid tenantId, string role)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A verified tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("A membership role is required.", nameof(role));
        TenantId = tenantId;
        Role = role;
    }
}

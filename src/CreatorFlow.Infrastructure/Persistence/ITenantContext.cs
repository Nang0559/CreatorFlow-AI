namespace CreatorFlow.Infrastructure.Persistence;

/// <summary>Verified workspace context for the current request or explicitly scoped background operation.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    string? Role { get; }
    bool IsResolved { get; }
}


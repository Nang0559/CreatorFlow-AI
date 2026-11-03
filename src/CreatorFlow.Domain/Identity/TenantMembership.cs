namespace CreatorFlow.Domain.Identity;

public enum TenantRole { Owner, Admin, Editor, Analyst, Publisher }
public enum TenantMembershipStatus { Active, Suspended }

/// <summary>
/// Maps an external identity-provider subject to a tenant. SubjectId is an immutable OIDC issuer subject,
/// not an email address or a caller-supplied tenant identifier.
/// </summary>
public sealed class TenantMembership
{
    private TenantMembership() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string IdentityIssuer { get; private set; } = string.Empty;
    public string SubjectId { get; private set; } = string.Empty;
    public string? VerifiedEmail { get; private set; }
    public TenantRole Role { get; private set; }
    public TenantMembershipStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TenantMembership Create(Guid tenantId, string identityIssuer, string subjectId,
        TenantRole role, DateTimeOffset now, string? verifiedEmail = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(identityIssuer) || identityIssuer.Trim().Length > 500)
            throw new ArgumentException("A valid identity issuer is required.", nameof(identityIssuer));
        if (string.IsNullOrWhiteSpace(subjectId) || subjectId.Trim().Length > 200)
            throw new ArgumentException("A valid identity subject is required.", nameof(subjectId));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));

        return new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IdentityIssuer = identityIssuer.Trim(),
            SubjectId = subjectId.Trim(),
            VerifiedEmail = NormalizeEmail(verifiedEmail),
            Role = role,
            Status = TenantMembershipStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var value = email.Trim().ToLowerInvariant();
        if (value.Length > 320 || !value.Contains('@', StringComparison.Ordinal))
            throw new ArgumentException("Verified email is invalid.", nameof(email));
        return value;
    }

    public void ChangeRole(TenantRole role, DateTimeOffset now)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (Status == TenantMembershipStatus.Suspended)
            throw new InvalidOperationException("A suspended membership cannot change role.");
        Role = role;
        UpdatedAt = now;
    }

    public void Suspend(DateTimeOffset now)
    {
        if (Status == TenantMembershipStatus.Suspended) return;
        Status = TenantMembershipStatus.Suspended;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        if (Status == TenantMembershipStatus.Active) return;
        Status = TenantMembershipStatus.Active;
        UpdatedAt = now;
    }
}

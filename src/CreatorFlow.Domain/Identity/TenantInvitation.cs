namespace CreatorFlow.Domain.Identity;

public enum TenantInvitationStatus { Pending, Accepted, Revoked }

/// <summary>A single-use, expiring invitation bound to one tenant and verified email address.</summary>
public sealed class TenantInvitation
{
    private TenantInvitation() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public TenantRole Role { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public string CreatedByIssuer { get; private set; } = string.Empty;
    public string CreatedBySubject { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public TenantInvitationStatus Status { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public string? AcceptedByIssuer { get; private set; }
    public string? AcceptedBySubject { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static TenantInvitation Create(Guid tenantId, string email, TenantRole role, string tokenHash,
        DateTimeOffset expiresAt, string issuer, string subject, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedEmail.Length is < 3 or > 320 || !normalizedEmail.Contains('@'))
            throw new ArgumentException("A valid invitation email is required.", nameof(email));
        if (role == TenantRole.Owner || !Enum.IsDefined(role))
            throw new ArgumentException("Owner cannot be granted by invitation.", nameof(role));
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64)
            throw new ArgumentException("A SHA-256 invitation token hash is required.", nameof(tokenHash));
        if (expiresAt <= now || expiresAt > now.AddDays(7))
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Invitation expiry must be within seven days.");
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 500 ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 200)
            throw new ArgumentException("A valid inviter identity is required.");
        return new TenantInvitation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = normalizedEmail, Role = role,
            TokenHash = tokenHash, ExpiresAt = expiresAt, CreatedByIssuer = issuer,
            CreatedBySubject = subject, CreatedAt = now, Status = TenantInvitationStatus.Pending
        };
    }

    public void Accept(string issuer, string subject, DateTimeOffset now)
    {
        if (Status != TenantInvitationStatus.Pending || ExpiresAt <= now || RevokedAt.HasValue)
            throw new InvalidOperationException("Invitation is no longer valid.");
        Status = TenantInvitationStatus.Accepted;
        AcceptedAt = now;
        AcceptedByIssuer = issuer;
        AcceptedBySubject = subject;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (Status != TenantInvitationStatus.Pending) return;
        Status = TenantInvitationStatus.Revoked;
        RevokedAt = now;
    }
}

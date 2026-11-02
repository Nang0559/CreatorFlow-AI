namespace CreatorFlow.Domain.Identity;

public enum TenantStatus { Active, Suspended }

/// <summary>A SaaS customer workspace. Tenant identity is independent from platform accounts.</summary>
public sealed class Tenant
{
    private Tenant() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Tenant Create(string name, string slug, DateTimeOffset now)
    {
        ValidateName(name);
        var normalizedSlug = NormalizeSlug(slug);
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = normalizedSlug,
            Status = TenantStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Rename(string name, DateTimeOffset now)
    {
        ValidateName(name);
        if (Status == TenantStatus.Suspended)
            throw new InvalidOperationException("A suspended tenant cannot be changed.");
        Name = name.Trim();
        UpdatedAt = now;
    }

    public void Suspend(DateTimeOffset now)
    {
        if (Status == TenantStatus.Suspended) return;
        Status = TenantStatus.Suspended;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        if (Status == TenantStatus.Active) return;
        Status = TenantStatus.Active;
        UpdatedAt = now;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
            throw new ArgumentException("Tenant name is required and must not exceed 160 characters.", nameof(name));
    }

    private static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Tenant slug is required.", nameof(slug));
        var value = slug.Trim().ToLowerInvariant();
        if (value.Length > 80 || value.Length < 3 ||
            value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) ||
            value[0] == '-' || value[^1] == '-' || value.Contains("--", StringComparison.Ordinal))
            throw new ArgumentException("Tenant slug must be 3–80 lowercase letters, digits or single hyphens.", nameof(slug));
        return value;
    }
}

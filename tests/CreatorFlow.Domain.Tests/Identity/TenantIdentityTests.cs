using CreatorFlow.Domain.Identity;

namespace CreatorFlow.Domain.Tests.Identity;

public sealed class TenantIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_slug_and_starts_active()
    {
        var tenant = Tenant.Create("Acme Studio", "  Acme-Studio  ", Now);

        Assert.Equal("Acme Studio", tenant.Name);
        Assert.Equal("acme-studio", tenant.Slug);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("-abc")]
    [InlineData("abc-")]
    [InlineData("a--b")]
    [InlineData("abc_def")]
    public void Create_rejects_invalid_slug(string slug)
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create("Acme", slug, Now));
    }

    [Fact]
    public void Suspended_tenant_cannot_be_renamed_and_can_be_reactivated()
    {
        var tenant = Tenant.Create("Acme", "acme", Now);
        tenant.Suspend(Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => tenant.Rename("New name", Now.AddMinutes(2)));
        tenant.Reactivate(Now.AddMinutes(3));

        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void Membership_binds_external_issuer_and_subject_to_role()
    {
        var tenantId = Guid.NewGuid();
        var membership = TenantMembership.Create(tenantId, "https://issuer.example", "oidc-sub-123", TenantRole.Owner, Now);

        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal("oidc-sub-123", membership.SubjectId);
        Assert.Equal(TenantRole.Owner, membership.Role);
        Assert.Equal(TenantMembershipStatus.Active, membership.Status);
    }

    [Fact]
    public void Membership_rejects_missing_identity_and_invalid_role()
    {
        Assert.Throws<ArgumentException>(() => TenantMembership.Create(Guid.NewGuid(), "issuer", " ", TenantRole.Owner, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => TenantMembership.Create(Guid.NewGuid(), "issuer", "subject", (TenantRole)999, Now));
    }

    [Fact]
    public void Suspended_membership_cannot_change_role()
    {
        var membership = TenantMembership.Create(Guid.NewGuid(), "issuer", "subject", TenantRole.Editor, Now);
        membership.Suspend(Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => membership.ChangeRole(TenantRole.Admin, Now.AddMinutes(2)));
    }
}

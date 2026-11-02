using CreatorFlow.Domain.Identity;
using Xunit;

namespace CreatorFlow.Domain.Tests.Identity;

public sealed class TenantInvitationTests
{
    [Fact]
    public void Invitation_normalizes_email_and_can_be_accepted_once()
    {
        var now = DateTimeOffset.UtcNow;
        var invitation = TenantInvitation.Create(Guid.NewGuid(), " Person@Example.com ",
            TenantRole.Editor, new string('A', 64), now.AddHours(12), "https://issuer.example", "subject-1", now);
        Assert.Equal("person@example.com", invitation.Email);
        invitation.Accept("https://issuer.example", "subject-2", now.AddMinutes(1));
        Assert.Equal(TenantInvitationStatus.Accepted, invitation.Status);
        Assert.Throws<InvalidOperationException>(() => invitation.Accept("https://issuer.example", "subject-3", now.AddMinutes(2)));
    }

    [Fact]
    public void Owner_role_cannot_be_granted_by_invitation()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => TenantInvitation.Create(Guid.NewGuid(), "owner@example.com",
            TenantRole.Owner, new string('B', 64), now.AddHours(1), "https://issuer.example", "subject", now));
    }

    [Fact]
    public void Expired_or_revoked_invitation_cannot_be_accepted()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = TenantInvitation.Create(Guid.NewGuid(), "person@example.com", TenantRole.Analyst,
            new string('C', 64), now.AddHours(1), "https://issuer.example", "subject", now);
        expired.Revoke(now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => expired.Accept("https://issuer.example", "invitee", now.AddMinutes(2)));
    }
}

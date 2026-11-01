using CreatorFlow.Domain.Content;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class StyleLearningProposalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Proposal_can_be_approved_once_and_profile_version_captures_approved_summary()
    {
        var profile = CreatorStyleProfile.Create("creator-main", "Kênh Việt", "Review chân thực", Now);
        var proposal = StyleLearningProposal.Create(profile.Id,
            "Mở đầu bằng tình huống thực tế; lời thoại tự nhiên; ưu tiên chứng minh lợi ích.",
            "[\"experiment-1\",\"experiment-2\"]", "{\"evidenceCount\":2}", "llm-synthesis-v1", Now.AddHours(1));

        Assert.Equal(StyleLearningProposalStatus.Pending, proposal.Status);
        proposal.Approve("creator-operator", "Đã kiểm tra bằng chứng.", Now.AddHours(2));
        profile.UpdateGuidance(profile.DisplayName, profile.Positioning, proposal.ProposedSummary, Now.AddHours(2));

        var version = CreatorStyleProfileVersion.Capture(profile, 1, proposal.Id, "creator-operator", Now.AddHours(2));

        Assert.Equal(StyleLearningProposalStatus.Approved, proposal.Status);
        Assert.Equal("creator-operator", proposal.ReviewerId);
        Assert.Equal(proposal.ProposedSummary, version.StyleSummary);
        Assert.Equal(proposal.Id, version.ApprovedProposalId);
        Assert.Equal(1, version.VersionNumber);
    }

    [Fact]
    public void Proposal_reject_requires_reason_and_terminal_states_cannot_be_reviewed_again()
    {
        var profile = CreatorStyleProfile.Create("creator-main", "Kênh Việt", null, Now);
        var proposal = StyleLearningProposal.Create(profile.Id, "Draft summary", "[\"exp-1\"]",
            "{\"signals\":[]}", "deterministic-v1", Now);

        Assert.Throws<ArgumentException>(() => proposal.Reject("operator", " ", Now.AddMinutes(1)));
        proposal.Reject("operator", "Không đủ mẫu đo.", Now.AddMinutes(2));
        Assert.Equal(StyleLearningProposalStatus.Rejected, proposal.Status);
        Assert.Throws<InvalidOperationException>(() => proposal.Approve("operator", null, Now.AddMinutes(3)));
    }

    [Fact]
    public void Proposal_validates_required_evidence_and_version_number()
    {
        var profile = CreatorStyleProfile.Create("creator-main", "Kênh Việt", null, Now);
        Assert.Throws<ArgumentException>(() => StyleLearningProposal.Create(profile.Id, " ", "[]", "{}", "v1", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatorStyleProfileVersion.Capture(profile, 0, null, "operator", Now));
    }
}

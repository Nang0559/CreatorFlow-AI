namespace CreatorFlow.Domain.Content;

public enum StyleLearningProposalStatus
{
    Pending,
    Approved,
    Rejected
}

public sealed class StyleLearningProposal
{
    private StyleLearningProposal() { }

    public Guid Id { get; private set; }
    public Guid CreatorStyleProfileId { get; private set; }
    public string ProposedSummary { get; private set; } = string.Empty;
    public string EvidenceExperimentIdsJson { get; private set; } = "[]";
    public string EvidenceSnapshotJson { get; private set; } = "{}";
    public string AlgorithmVersion { get; private set; } = string.Empty;
    public StyleLearningProposalStatus Status { get; private set; }
    public string? ReviewReason { get; private set; }
    public string? ReviewerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    public static StyleLearningProposal Create(Guid profileId, string proposedSummary,
        string evidenceExperimentIdsJson, string evidenceSnapshotJson, string algorithmVersion,
        DateTimeOffset now)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("Profile is required.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(proposedSummary) || proposedSummary.Trim().Length > 4000)
            throw new ArgumentException("Proposed summary is required and cannot exceed 4000 characters.", nameof(proposedSummary));
        if (string.IsNullOrWhiteSpace(evidenceExperimentIdsJson) || evidenceExperimentIdsJson.Length > 20000)
            throw new ArgumentException("Evidence experiment IDs are required.", nameof(evidenceExperimentIdsJson));
        if (string.IsNullOrWhiteSpace(evidenceSnapshotJson) || evidenceSnapshotJson.Length > 100000)
            throw new ArgumentException("Evidence snapshot is required.", nameof(evidenceSnapshotJson));
        if (string.IsNullOrWhiteSpace(algorithmVersion) || algorithmVersion.Trim().Length > 80)
            throw new ArgumentException("Algorithm version is required.", nameof(algorithmVersion));
        return new StyleLearningProposal
        {
            Id = Guid.NewGuid(), CreatorStyleProfileId = profileId,
            ProposedSummary = proposedSummary.Trim(),
            EvidenceExperimentIdsJson = evidenceExperimentIdsJson,
            EvidenceSnapshotJson = evidenceSnapshotJson,
            AlgorithmVersion = algorithmVersion.Trim(),
            Status = StyleLearningProposalStatus.Pending, CreatedAt = now
        };
    }

    public void Approve(string reviewerId, string? reason, DateTimeOffset now)
    {
        EnsurePending();
        ValidateReview(reviewerId, reason);
        Status = StyleLearningProposalStatus.Approved;
        ReviewerId = reviewerId.Trim();
        ReviewReason = reason?.Trim();
        ReviewedAt = now;
    }

    public void Reject(string reviewerId, string? reason, DateTimeOffset now)
    {
        EnsurePending();
        ValidateReview(reviewerId, reason);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required when rejecting a learning proposal.", nameof(reason));
        Status = StyleLearningProposalStatus.Rejected;
        ReviewerId = reviewerId.Trim();
        ReviewReason = reason.Trim();
        ReviewedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != StyleLearningProposalStatus.Pending)
            throw new InvalidOperationException("Only pending learning proposals can be reviewed.");
    }

    private static void ValidateReview(string reviewerId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reviewerId) || reviewerId.Trim().Length > 160)
            throw new ArgumentException("Reviewer ID is required and cannot exceed 160 characters.", nameof(reviewerId));
        if (reason?.Length > 2000) throw new ArgumentException("Review reason cannot exceed 2000 characters.", nameof(reason));
    }
}

public sealed class CreatorStyleProfileVersion
{
    private CreatorStyleProfileVersion() { }

    public Guid Id { get; private set; }
    public Guid CreatorStyleProfileId { get; private set; }
    public Guid? ApprovedProposalId { get; private set; }
    public int VersionNumber { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Positioning { get; private set; } = string.Empty;
    public string StyleSummary { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    public static CreatorStyleProfileVersion Capture(CreatorStyleProfile profile, int versionNumber,
        Guid? approvedProposalId, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 160)
            throw new ArgumentException("Created by is required.", nameof(createdBy));
        return new CreatorStyleProfileVersion
        {
            Id = Guid.NewGuid(), CreatorStyleProfileId = profile.Id,
            ApprovedProposalId = approvedProposalId, VersionNumber = versionNumber,
            DisplayName = profile.DisplayName, Positioning = profile.Positioning,
            StyleSummary = profile.StyleSummary, CreatedAt = now, CreatedBy = createdBy.Trim()
        };
    }
}
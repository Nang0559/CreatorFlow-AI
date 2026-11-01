namespace CreatorFlow.Domain.Discovery;

public enum TrendSelectionDecision { Select, Reject, Watch }

/// <summary>Immutable creator-authored feedback; model features never mutate the decision.</summary>
public sealed class TrendSelectionFeedback
{
    private TrendSelectionFeedback() { }
    public Guid Id { get; private set; }
    public Guid DouyinVideoCandidateId { get; private set; }
    public TrendSelectionDecision Decision { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string ReasonNote { get; private set; } = string.Empty;
    public string CandidateSnapshotJson { get; private set; } = "{}";
    public string FeatureSnapshotJson { get; private set; } = "{}";
    public DateTimeOffset ReviewedAt { get; private set; }

    public static TrendSelectionFeedback Create(Guid candidateId, TrendSelectionDecision decision,
        string reasonCode, string? reasonNote, string candidateSnapshotJson, string featureSnapshotJson, DateTimeOffset reviewedAt)
    {
        if (candidateId == Guid.Empty) throw new ArgumentException("Candidate is required.", nameof(candidateId));
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Trim().Length > 60)
            throw new ArgumentException("A reason code of at most 60 characters is required.", nameof(reasonCode));
        if (reasonNote?.Length > 2000) throw new ArgumentException("Reason note cannot exceed 2000 characters.", nameof(reasonNote));
        if (string.IsNullOrWhiteSpace(candidateSnapshotJson) || candidateSnapshotJson.Length > 50000)
            throw new ArgumentException("Candidate snapshot is required.", nameof(candidateSnapshotJson));
        if (string.IsNullOrWhiteSpace(featureSnapshotJson) || featureSnapshotJson.Length > 50000)
            throw new ArgumentException("Feature snapshot is required.", nameof(featureSnapshotJson));
        return new TrendSelectionFeedback
        {
            Id = Guid.NewGuid(), DouyinVideoCandidateId = candidateId, Decision = decision,
            ReasonCode = reasonCode.Trim(), ReasonNote = reasonNote ?? string.Empty,
            CandidateSnapshotJson = candidateSnapshotJson, FeatureSnapshotJson = featureSnapshotJson, ReviewedAt = reviewedAt
        };
    }
}

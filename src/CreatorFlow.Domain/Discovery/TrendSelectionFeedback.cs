using System.Text.Json;

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
        if (!Enum.IsDefined(decision))
            throw new ArgumentOutOfRangeException(nameof(decision), "Use Select, Reject or Watch.");
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Trim().Length > 60)
            throw new ArgumentException("A reason code of at most 60 characters is required.", nameof(reasonCode));
        if (reasonNote?.Length > 2000) throw new ArgumentException("Reason note cannot exceed 2000 characters.", nameof(reasonNote));

        ValidateJsonObject(candidateSnapshotJson, nameof(candidateSnapshotJson), "Candidate snapshot");
        ValidateJsonObject(featureSnapshotJson, nameof(featureSnapshotJson), "Feature snapshot");

        return new TrendSelectionFeedback
        {
            Id = Guid.NewGuid(), DouyinVideoCandidateId = candidateId, Decision = decision,
            ReasonCode = reasonCode.Trim(), ReasonNote = reasonNote ?? string.Empty,
            CandidateSnapshotJson = candidateSnapshotJson, FeatureSnapshotJson = featureSnapshotJson, ReviewedAt = reviewedAt
        };
    }

    private static void ValidateJsonObject(string json, string parameterName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 50000)
            throw new ArgumentException($"{displayName} is required and must not exceed 50000 characters.", parameterName);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"{displayName} must be a JSON object.", parameterName);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{displayName} must contain valid JSON.", parameterName, ex);
        }
    }
}

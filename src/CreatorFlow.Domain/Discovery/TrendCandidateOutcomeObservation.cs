using System.Text.Json;

namespace CreatorFlow.Domain.Discovery;

public enum TrendCandidateOutcomeKind { Adapted, Published, MarketPerformance, CreatorAssessment }

/// <summary>Append-only evidence about what happened after a creator's selection decision.</summary>
public sealed class TrendCandidateOutcomeObservation
{
    private TrendCandidateOutcomeObservation() { }

    public Guid Id { get; private set; }
    public Guid FeedbackId { get; private set; }
    public TrendCandidateOutcomeKind OutcomeKind { get; private set; }
    public string? OutcomeValue { get; private set; }
    public string MetricsSnapshotJson { get; private set; } = "{}";
    public string Source { get; private set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; private set; }

    public static TrendCandidateOutcomeObservation Create(Guid feedbackId, TrendCandidateOutcomeKind outcomeKind,
        string? outcomeValue, string metricsSnapshotJson, string source, DateTimeOffset observedAt)
    {
        if (feedbackId == Guid.Empty) throw new ArgumentException("Feedback is required.", nameof(feedbackId));
        if (!Enum.IsDefined(outcomeKind))
            throw new ArgumentOutOfRangeException(nameof(outcomeKind), "Use Adapted, Published, MarketPerformance or CreatorAssessment.");
        if (outcomeValue?.Length > 1000)
            throw new ArgumentException("Outcome value cannot exceed 1000 characters.", nameof(outcomeValue));
        if (string.IsNullOrWhiteSpace(source) || source.Trim().Length > 80)
            throw new ArgumentException("An outcome source of at most 80 characters is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(metricsSnapshotJson) || metricsSnapshotJson.Length > 50000)
            throw new ArgumentException("Metrics snapshot is required and must not exceed 50000 characters.", nameof(metricsSnapshotJson));

        try
        {
            using var document = JsonDocument.Parse(metricsSnapshotJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Metrics snapshot must be a JSON object.", nameof(metricsSnapshotJson));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Metrics snapshot must contain valid JSON.", nameof(metricsSnapshotJson), ex);
        }

        return new TrendCandidateOutcomeObservation
        {
            Id = Guid.NewGuid(),
            FeedbackId = feedbackId,
            OutcomeKind = outcomeKind,
            OutcomeValue = outcomeValue,
            MetricsSnapshotJson = metricsSnapshotJson,
            Source = source.Trim(),
            ObservedAt = observedAt
        };
    }
}

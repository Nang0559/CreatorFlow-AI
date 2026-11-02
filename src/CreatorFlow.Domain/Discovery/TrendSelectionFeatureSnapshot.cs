using System.Text.Json;

namespace CreatorFlow.Domain.Discovery;

/// <summary>
/// Versioned, deterministic metadata features captured at creator review time.
/// The creator's decision and mutable candidate status are deliberately excluded to prevent label leakage.
/// </summary>
public sealed record TrendSelectionFeatureSnapshot(
    int SchemaVersion,
    DateTimeOffset ExtractedAt,
    string DiscoveryMethod,
    bool HasSourceChannel,
    bool HasTopic,
    int TitleCharacterCount,
    int TitleTokenCount,
    bool TitleHasQuestionMark,
    bool TitleHasDigits,
    int TitleHashtagCount,
    double? PublishedAgeHours,
    double DiscoveredAgeHours)
{
    public const int CurrentSchemaVersion = 1;
    public const string SchemaName = "creatorflow.trend-selection";

    public static TrendSelectionFeatureSnapshot Create(DouyinVideoCandidate candidate, DateTimeOffset extractedAt)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var title = candidate.Title ?? string.Empty;
        var tokens = title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var publishedAge = candidate.PublishedAt.HasValue
            ? (double?)Math.Round((extractedAt - candidate.PublishedAt.Value).TotalHours, 3, MidpointRounding.AwayFromZero)
            : null;

        return new TrendSelectionFeatureSnapshot(
            CurrentSchemaVersion,
            extractedAt,
            candidate.DiscoveryMethod.ToString(),
            candidate.SourceChannelId.HasValue,
            candidate.ContentTopicId.HasValue,
            title.Length,
            tokens.Length,
            title.Contains('?') || title.Contains('？'),
            title.Any(char.IsDigit),
            tokens.Count(token => token.StartsWith('#')),
            publishedAge,
            Math.Round((extractedAt - candidate.DiscoveredAt).TotalHours, 3, MidpointRounding.AwayFromZero));
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        schema = SchemaName,
        schemaVersion = SchemaVersion,
        extractedAt = ExtractedAt,
        discoveryMethod = DiscoveryMethod,
        hasSourceChannel = HasSourceChannel,
        hasTopic = HasTopic,
        titleCharacterCount = TitleCharacterCount,
        titleTokenCount = TitleTokenCount,
        titleHasQuestionMark = TitleHasQuestionMark,
        titleHasDigits = TitleHasDigits,
        titleHashtagCount = TitleHashtagCount,
        publishedAgeHours = PublishedAgeHours,
        discoveredAgeHours = DiscoveredAgeHours
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

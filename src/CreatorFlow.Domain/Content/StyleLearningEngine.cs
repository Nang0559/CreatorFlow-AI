namespace CreatorFlow.Domain.Content;

public sealed record StyleFactorInsight(
    string Dimension,
    string Value,
    int SupportingExperiments,
    int WinningExperiments,
    int LosingExperiments,
    decimal WinRate,
    decimal Lift);

public sealed record StyleTopicInsight(
    string Topic,
    int EligibleExperiments,
    IReadOnlyList<StyleFactorInsight> StrongestSignals);

public sealed record StyleLearningAnalysis(
    string? TopicFilter,
    int EligibleExperiments,
    int EligibleVariants,
    int ExcludedExperiments,
    decimal BaselineWinRate,
    IReadOnlyList<StyleFactorInsight> StrongestSignals,
    IReadOnlyList<StyleTopicInsight> TopicBreakdown,
    string SuggestedStyleSummary,
    string Methodology);

/// <summary>
/// Transparent, deterministic analysis of measured A/B/C experiments.
/// This is deliberately not an LLM-generated verdict: every signal can be traced
/// to evaluated experiments and a confirmed winner.
/// </summary>
public static class StyleLearningEngine
{
    public static StyleLearningAnalysis Analyze(
        IEnumerable<StyleExperiment> source,
        string? topic = null,
        long minimumViewsPerVariant = 100)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (minimumViewsPerVariant < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumViewsPerVariant));

        var all = source.Where(x => x.Status == StyleExperimentStatus.Evaluated &&
                                    x.WinningVariantId.HasValue).ToArray();
        var scoped = all.Where(x => string.IsNullOrWhiteSpace(topic) ||
                                     string.Equals(x.Topic, topic.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var eligible = scoped.Where(x => x.Variants.Count is >= 2 and <= 3 &&
                                         x.Variants.All(v => v.Status == StyleVariantStatus.Measured &&
                                                             v.Views >= minimumViewsPerVariant))
            .ToArray();

        var insights = BuildSignals(eligible);
        var topicBreakdown = eligible
            .Where(x => !string.IsNullOrWhiteSpace(x.Topic))
            .GroupBy(x => x.Topic!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new StyleTopicInsight(
                group.Key,
                group.Count(),
                BuildSignals(group).Take(5).ToArray()))
            .OrderByDescending(x => x.EligibleExperiments)
            .ThenBy(x => x.Topic, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var baseline = eligible.Length == 0
            ? 0m
            : eligible.Average(x => 1m / x.Variants.Count);

        var summary = insights.Count == 0
            ? "Chưa đủ bằng chứng lặp lại để đề xuất thay đổi phong cách. Hãy tiếp tục đo lường các thử nghiệm đã xuất bản."
            : "Các tín hiệu lặp lại từ thử nghiệm: " +
              string.Join("; ", insights.Take(4).Select(x =>
                  $"{x.Dimension}: {x.Value} (thắng {x.WinningExperiments}/{x.SupportingExperiments}, uplift {(x.Lift * 100m):0.#} điểm %)")) +
              ". Đây là đề xuất cần người sáng tạo duyệt, không phải thay đổi tự động.";

        return new StyleLearningAnalysis(
            string.IsNullOrWhiteSpace(topic) ? null : topic.Trim(),
            eligible.Length,
            eligible.Sum(x => x.Variants.Count),
            scoped.Length - eligible.Length,
            decimal.Round(baseline, 4),
            insights,
            topicBreakdown,
            summary,
            "Chỉ dùng thử nghiệm Evaluated có winner đã xác nhận; mọi biến thể phải có snapshot đo lường và đạt ngưỡng lượt xem. Mỗi đặc điểm được so sánh theo số thử nghiệm mà nó xuất hiện ở biến thể thắng so với thua. Lift = win rate của đặc điểm trừ baseline 1/N biến thể. Không suy luận nhân quả và không tự động sửa hồ sơ.");
    }

    private static IReadOnlyList<StyleFactorInsight> BuildSignals(IEnumerable<StyleExperiment> experiments)
    {
        var counts = new Dictionary<(string Dimension, string Value),
            (HashSet<Guid> Wins, HashSet<Guid> Losses)>();
        var materialized = experiments.ToArray();
        foreach (var experiment in materialized)
        {
            foreach (var variant in experiment.Variants)
            {
                Add("Hook", variant.Hook, experiment, variant);
                Add("SpeakingStyle", variant.SpeakingStyle, experiment, variant);
                Add("EditPacing", variant.EditPacing, experiment, variant);
                Add("AudioApproach", variant.AudioApproach, experiment, variant);
            }
        }

        var baseline = materialized.Length == 0 ? 0m : materialized.Average(x => 1m / x.Variants.Count);
        return counts.Select(pair =>
            {
                // Experiments where the same factor occurs in both a winner and a loser
                // are ambiguous and are excluded from the decisive support denominator.
                var decisiveWins = pair.Value.Wins.Except(pair.Value.Losses).Count();
                var decisiveLosses = pair.Value.Losses.Except(pair.Value.Wins).Count();
                var decisiveSupport = decisiveWins + decisiveLosses;
                var winRate = decisiveSupport == 0 ? 0m : (decimal)decisiveWins / decisiveSupport;
                return new StyleFactorInsight(pair.Key.Dimension, pair.Key.Value, decisiveSupport,
                    decisiveWins, decisiveLosses, decimal.Round(winRate, 4),
                    decimal.Round(winRate - baseline, 4));
            })
            .Where(x => x.SupportingExperiments >= 2 && x.WinningExperiments >= 2 && x.Lift > 0m)
            .OrderByDescending(x => x.Lift)
            .ThenByDescending(x => x.SupportingExperiments)
            .ThenBy(x => x.Dimension)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        void Add(string dimension, string? rawValue, StyleExperiment experiment, StyleExperimentVariant variant)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return;
            var key = (dimension, rawValue.Trim());
            if (!counts.TryGetValue(key, out var current))
                current = (new HashSet<Guid>(), new HashSet<Guid>());
            if (variant.Id == experiment.WinningVariantId)
                current.Wins.Add(experiment.Id);
            else
                current.Losses.Add(experiment.Id);
            counts[key] = current;
        }
    }
}

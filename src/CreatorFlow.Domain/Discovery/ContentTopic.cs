namespace CreatorFlow.Domain.Discovery;

public sealed class ContentTopic
{
    private ContentTopic() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Keywords { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ContentTopic Create(string name, string? keywords, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Topic name is required and cannot exceed 120 characters.", nameof(name));
        return new ContentTopic
        {
            Id = Guid.NewGuid(), Name = name.Trim(),
            Keywords = NormalizeKeywords(keywords), IsActive = true,
            CreatedAt = now, UpdatedAt = now
        };
    }

    public void Update(string name, string? keywords, DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("Inactive topics cannot be updated.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Topic name is required and cannot exceed 120 characters.", nameof(name));
        Name = name.Trim();
        Keywords = NormalizeKeywords(keywords);
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = now;
    }

    private static string NormalizeKeywords(string? keywords) =>
        string.Join("\n", (keywords ?? string.Empty).Split(new[] { '\r', '\n', ',', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length <= 100).Distinct(StringComparer.OrdinalIgnoreCase));
}

namespace CreatorFlow.Domain.Content;

public enum ContentProjectStatus
{
    Draft, Analyzing, ScriptReview, MediaReady, Editing,
    FinalReview, Approved, Published, Failed, Archived
}

public sealed class ContentProject
{
    private ContentProject() { }
    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public ContentProjectStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ContentProject Create(string title, DateTimeOffset now)
    {
        ValidateTitle(title);
        return new ContentProject { Id = Guid.NewGuid(), Title = title.Trim(), Status = ContentProjectStatus.Draft, CreatedAt = now, UpdatedAt = now };
    }

    public void Rename(string title, DateTimeOffset now)
    {
        ValidateTitle(title);
        if (Status is ContentProjectStatus.Published or ContentProjectStatus.Archived)
            throw new InvalidOperationException("Published or archived projects cannot be renamed.");
        Title = title.Trim();
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        if (Status == ContentProjectStatus.Published)
            throw new InvalidOperationException("Published projects cannot be archived through this operation.");
        if (Status == ContentProjectStatus.Archived) return;
        Status = ContentProjectStatus.Archived;
        UpdatedAt = now;
    }

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A project title is required.", nameof(title));
        if (title.Trim().Length > 200)
            throw new ArgumentException("A project title cannot exceed 200 characters.", nameof(title));
    }
}

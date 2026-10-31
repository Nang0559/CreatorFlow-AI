using CreatorFlow.Domain.Content;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class ContentProjectTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_sets_draft_state_and_normalizes_title()
    {
        var project = ContentProject.Create("  Launch video  ", Now);

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("Launch video", project.Title);
        Assert.Equal(ContentProjectStatus.Draft, project.Status);
        Assert.Equal(Now, project.CreatedAt);
        Assert.Equal(Now, project.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_title(string title)
    {
        Assert.Throws<ArgumentException>(() => ContentProject.Create(title, Now));
    }

    [Fact]
    public void Create_rejects_title_longer_than_200_characters()
    {
        Assert.Throws<ArgumentException>(() => ContentProject.Create(new string('x', 201), Now));
    }

    [Fact]
    public void Rename_updates_title_and_timestamp()
    {
        var project = ContentProject.Create("Before", Now);
        var changedAt = Now.AddMinutes(5);

        project.Rename("After", changedAt);

        Assert.Equal("After", project.Title);
        Assert.Equal(changedAt, project.UpdatedAt);
    }

    [Fact]
    public void Archive_moves_project_to_archived()
    {
        var project = ContentProject.Create("Draft", Now);
        var changedAt = Now.AddDays(1);

        project.Archive(changedAt);

        Assert.Equal(ContentProjectStatus.Archived, project.Status);
        Assert.Equal(changedAt, project.UpdatedAt);
    }

    [Fact]
    public void Archived_project_cannot_be_renamed()
    {
        var project = ContentProject.Create("Draft", Now);
        project.Archive(Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => project.Rename("New title", Now.AddMinutes(2)));
    }
}

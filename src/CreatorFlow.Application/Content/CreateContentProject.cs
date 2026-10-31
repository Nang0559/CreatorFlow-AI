using CreatorFlow.Domain.Content;

namespace CreatorFlow.Application.Content;

public sealed record CreateContentProjectCommand(string Title);
public sealed record ContentProjectDto(Guid Id, string Title, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IContentProjectRepository
{
    Task AddAsync(ContentProject project, CancellationToken cancellationToken);
    Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContentProject>> ListAsync(int skip, int take, CancellationToken cancellationToken);
}

public sealed class CreateContentProjectHandler(IContentProjectRepository repository)
{
    public async Task<ContentProjectDto> HandleAsync(CreateContentProjectCommand command, CancellationToken cancellationToken = default)
    {
        var project = ContentProject.Create(command.Title, DateTimeOffset.UtcNow);
        await repository.AddAsync(project, cancellationToken);
        return ToDto(project);
    }

    internal static ContentProjectDto ToDto(ContentProject project) =>
        new(project.Id, project.Title, project.Status.ToString(), project.CreatedAt, project.UpdatedAt);
}

public sealed class ListContentProjectsHandler(IContentProjectRepository repository)
{
    public async Task<IReadOnlyList<ContentProjectDto>> HandleAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
        if (take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));
        var projects = await repository.ListAsync(skip, take, cancellationToken);
        return projects.Select(CreateContentProjectHandler.ToDto).ToArray();
    }
}

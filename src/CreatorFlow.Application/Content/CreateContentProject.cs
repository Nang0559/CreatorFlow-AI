using CreatorFlow.Domain.Content;

namespace CreatorFlow.Application.Content;

public sealed record CreateContentProjectCommand(string Title);
public sealed record ContentProjectDto(Guid Id, string Title, string Status, DateTimeOffset CreatedAt);

public interface IContentProjectRepository
{
    Task AddAsync(ContentProject project, CancellationToken cancellationToken);
}

public sealed class CreateContentProjectHandler(IContentProjectRepository repository)
{
    public async Task<ContentProjectDto> HandleAsync(
        CreateContentProjectCommand command, CancellationToken cancellationToken = default)
    {
        var project = ContentProject.Create(command.Title, DateTimeOffset.UtcNow);
        await repository.AddAsync(project, cancellationToken);
        return new ContentProjectDto(project.Id, project.Title, project.Status.ToString(), project.CreatedAt);
    }
}

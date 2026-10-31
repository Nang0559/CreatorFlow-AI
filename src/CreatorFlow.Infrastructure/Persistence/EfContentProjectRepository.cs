using CreatorFlow.Application.Content;
using CreatorFlow.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Infrastructure.Persistence;

public sealed class EfContentProjectRepository(CreatorFlowDbContext dbContext) : IContentProjectRepository
{
    public async Task AddAsync(ContentProject project, CancellationToken cancellationToken)
    {
        await dbContext.ContentProjects.AddAsync(project, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ContentProjects.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContentProject>> ListAsync(int skip, int take, CancellationToken cancellationToken) =>
        await dbContext.ContentProjects.AsNoTracking().OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Skip(skip).Take(take).ToArrayAsync(cancellationToken);
}

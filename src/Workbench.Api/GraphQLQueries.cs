using HotChocolate;
using Microsoft.EntityFrameworkCore;
using Workbench.Core;
using Workbench.Infrastructure;

namespace Workbench.Api;

public record ImageSummary(
    Guid Id,
    string OriginalFileName,
    int Width,
    int Height,
    string FileFormat,
    long FileSize,
    string CollectionName,
    DateTime ImportedAt);

public class GraphQLQueries
{
    public async Task<List<ImageSummary>> GetImagesAsync(
        [Service] IDbContextFactory<WorkbenchDbContext> factory,
        CancellationToken cancellationToken,
        string? search = null,
        int skip = 0,
        int take = 50)
    {
        if (skip < 0 || take < 1 || take > 100)
        {
            throw new GraphQLException(
                "Skip must be nonnegative and take must be 1–100.");
        }

        await using var db =
            await factory.CreateDbContextAsync(cancellationToken);

        var query = db.Images.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(image =>
                image.OriginalFileName.Contains(term) ||
                image.CollectionName.Contains(term));
        }

        return await query
            .OrderByDescending(image => image.ImportedAt)
            .ThenBy(image => image.Id)
            .Skip(skip)
            .Take(take)
            .Select(image => new ImageSummary(
                image.Id,
                image.OriginalFileName,
                image.Width,
                image.Height,
                image.FileFormat,
                image.FileSize,
                image.CollectionName,
                image.ImportedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ImageSummary?> GetImageAsync(
        Guid id,
        [Service] IDbContextFactory<WorkbenchDbContext> factory,
        CancellationToken cancellationToken)
    {
        await using var db =
            await factory.CreateDbContextAsync(cancellationToken);

        return await db.Images
            .AsNoTracking()
            .Where(image => image.Id == id)
            .Select(image => new ImageSummary(
                image.Id,
                image.OriginalFileName,
                image.Width,
                image.Height,
                image.FileFormat,
                image.FileSize,
                image.CollectionName,
                image.ImportedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Annotation>> GetAnnotationsAsync(
        Guid imageId,
        [Service] IDbContextFactory<WorkbenchDbContext> factory,
        CancellationToken cancellationToken)
    {
        await using var db =
            await factory.CreateDbContextAsync(cancellationToken);

        return await db.Annotations
            .AsNoTracking()
            .Where(annotation => annotation.ImageId == imageId)
            .OrderBy(annotation => annotation.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
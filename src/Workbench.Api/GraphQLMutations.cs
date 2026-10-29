using HotChocolate;
using Microsoft.EntityFrameworkCore;
using Workbench.Core;
using Workbench.Infrastructure;

namespace Workbench.Api;

public class GraphQLMutations
{
    public async Task<Annotation> AddAnnotationAsync(
        Guid imageId,
        AnnotationInput input,
        [Service] IDbContextFactory<WorkbenchDbContext> factory,
        CancellationToken cancellationToken)
    {
        await using var db =
            await factory.CreateDbContextAsync(cancellationToken);

        var image = await db.Images
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == imageId,
                cancellationToken);

        if (image is null)
            throw new GraphQLException("Image not found.");

        if (string.IsNullOrWhiteSpace(input.Label) ||
            input.Label.Length > 200)
        {
            throw new GraphQLException(
                "A label of 1–200 characters is required.");
        }

        if ((input.Notes?.Length ?? 0) > 4000)
        {
            throw new GraphQLException(
                "Notes cannot exceed 4000 characters.");
        }

        if (!double.IsFinite(input.X) ||
            !double.IsFinite(input.Y) ||
            !double.IsFinite(input.Width) ||
            !double.IsFinite(input.Height))
        {
            throw new GraphQLException(
                "Coordinates must be finite numbers.");
        }

        if (input.X < 0 ||
            input.Y < 0 ||
            input.Width <= 0 ||
            input.Height <= 0 ||
            input.Width > image.Width ||
            input.Height > image.Height ||
            input.X > image.Width - input.Width ||
            input.Y > image.Height - input.Height)
        {
            throw new GraphQLException(
                "The rectangle must fit inside the original image.");
        }

        var annotation = new Annotation
        {
            ImageId = imageId,
            Label = input.Label!.Trim(),
            Notes = input.Notes?.Trim() ?? "",
            X = input.X,
            Y = input.Y,
            Width = input.Width,
            Height = input.Height
        };

        db.Annotations.Add(annotation);

        await db.SaveChangesAsync(cancellationToken);

        return annotation;
    }

    public async Task<ImageSummary> UpdateImageMetadataAsync(
        Guid id,
        string originalFileName,
        string collectionName,
        [Service] IDbContextFactory<WorkbenchDbContext> factory,
        CancellationToken cancellationToken)
    {
        var fileName = originalFileName.Trim();
        var collection = collectionName.Trim();

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255)
        {
            throw new GraphQLException(
                "Filename must contain 1–255 characters.");
        }

        if (fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.Any(char.IsControl))
        {
            throw new GraphQLException(
                "Enter a filename, not a path.");
        }

        if (collection.Length > 200 ||
            collection.Any(char.IsControl))
        {
            throw new GraphQLException(
                "Collection must contain at most 200 characters " +
                "and cannot contain control characters.");
        }

        await using var db =
            await factory.CreateDbContextAsync(cancellationToken);

        var image = await db.Images
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (image is null)
            throw new GraphQLException("Image not found.");

        // Change metadata only. The managed file location,
        // file format, hash, and annotations remain intact.
        image.OriginalFileName = fileName;
        image.CollectionName = collection;

        await db.SaveChangesAsync(cancellationToken);

        return new ImageSummary(
            image.Id,
            image.OriginalFileName,
            image.Width,
            image.Height,
            image.FileFormat,
            image.FileSize,
            image.CollectionName,
            image.ImportedAt);
    }
}
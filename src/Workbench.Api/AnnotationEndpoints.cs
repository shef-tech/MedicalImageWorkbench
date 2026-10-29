using Microsoft.EntityFrameworkCore;
using Workbench.Core;
using Workbench.Infrastructure;

namespace Workbench.Api;

public record AnnotationInput(
    string? Label,
    string? Notes,
    double X,
    double Y,
    double Width,
    double Height);

public static class AnnotationEndpoints
{
    public static void MapAnnotationEndpoints(
        this WebApplication app)
    {
        // Retrieve annotations for an image.
        app.MapGet("/images/{imageId:guid}/annotations", async (
            Guid imageId,
            WorkbenchDbContext db,
            CancellationToken cancellationToken) =>
        {
            var imageExists = await db.Images.AnyAsync(
                image => image.Id == imageId,
                cancellationToken);

            if (!imageExists)
                return Results.NotFound("Image not found.");

            var annotations = await db.Annotations
                .AsNoTracking()
                .Where(annotation => annotation.ImageId == imageId)
                .OrderBy(annotation => annotation.CreatedAt)
                .ToListAsync(cancellationToken);

            return Results.Ok(annotations);
        });

        // Create an annotation.
        app.MapPost("/images/{imageId:guid}/annotations", async (
            Guid imageId,
            AnnotationInput input,
            WorkbenchDbContext db,
            CancellationToken cancellationToken) =>
        {
            var image = await db.Images
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == imageId,
                    cancellationToken);

            if (image is null)
                return Results.NotFound("Image not found.");

            var error = Validate(input, image);

            if (error is not null)
                return Results.BadRequest(error);

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

            return Results.Created(
                $"/images/{imageId}/annotations/{annotation.Id}",
                annotation);
        });

        // Edit an existing annotation.
        app.MapPut(
            "/images/{imageId:guid}/annotations/{annotationId:guid}",
            async (
                Guid imageId,
                Guid annotationId,
                AnnotationInput input,
                WorkbenchDbContext db,
                CancellationToken cancellationToken) =>
        {
            var annotation = await db.Annotations
                .SingleOrDefaultAsync(
                    item => item.Id == annotationId &&
                            item.ImageId == imageId,
                    cancellationToken);

            if (annotation is null)
                return Results.NotFound("Annotation not found.");

            var image = await db.Images
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == imageId,
                    cancellationToken);

            if (image is null)
                return Results.NotFound("Image not found.");

            var error = Validate(input, image);

            if (error is not null)
                return Results.BadRequest(error);

            annotation.Label = input.Label!.Trim();
            annotation.Notes = input.Notes?.Trim() ?? "";
            annotation.X = input.X;
            annotation.Y = input.Y;
            annotation.Width = input.Width;
            annotation.Height = input.Height;
            annotation.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(annotation);
        });

        // Delete an annotation.
        app.MapDelete(
            "/images/{imageId:guid}/annotations/{annotationId:guid}",
            async (
                Guid imageId,
                Guid annotationId,
                WorkbenchDbContext db,
                CancellationToken cancellationToken) =>
        {
            var annotation = await db.Annotations
                .SingleOrDefaultAsync(
                    item => item.Id == annotationId &&
                            item.ImageId == imageId,
                    cancellationToken);

            if (annotation is null)
                return Results.NotFound("Annotation not found.");

            db.Annotations.Remove(annotation);

            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });
    }

    private static string? Validate(
        AnnotationInput input,
        ImageRecord image)
    {
        if (string.IsNullOrWhiteSpace(input.Label) ||
            input.Label.Length > 200)
        {
            return "A label of 1–200 characters is required.";
        }

        if ((input.Notes?.Length ?? 0) > 4000)
            return "Notes cannot exceed 4000 characters.";

        if (!double.IsFinite(input.X) ||
            !double.IsFinite(input.Y) ||
            !double.IsFinite(input.Width) ||
            !double.IsFinite(input.Height))
        {
            return "Coordinates must be finite numbers.";
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
            return "The rectangle must fit inside the original image.";
        }

        return null;
    }
}
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Workbench.Infrastructure;

namespace Workbench.Api;

public static class ProcessingEndpoints
{
    public static void MapProcessingEndpoints(
        this WebApplication app)
    {
        app.MapPost("/images/{id:guid}/grayscale", async (
            Guid id,
            WorkbenchDbContext db,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            float[] matrix =
            {
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0,       0,       0,       1, 0
            };

            return await ProcessAsync(
                id, "grayscale", matrix,
                db, environment, cancellationToken);
        });

        app.MapPost("/images/{id:guid}/adjust", async (
            Guid id,
            float brightness,
            float contrast,
            WorkbenchDbContext db,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (!float.IsFinite(brightness) ||
                brightness < -100 || brightness > 100)
            {
                return Results.BadRequest(
                    "Brightness must be between -100 and 100.");
            }

            if (!float.IsFinite(contrast) ||
                contrast < 0.1f || contrast > 3f)
            {
                return Results.BadRequest(
                    "Contrast must be between 0.1 and 3.");
            }

            // Adjust contrast around the midpoint, then brightness.
            var offset = (128f * (1f - contrast) + brightness) / 255f;

            float[] matrix =
            {
                contrast, 0,        0,        0, offset,
                0,        contrast, 0,        0, offset,
                0,        0,        contrast, 0, offset,
                0,        0,        0,        1, 0
            };

            return await ProcessAsync(
                id, "adjusted", matrix,
                db, environment, cancellationToken);
        });
    }

    private static async Task<IResult> ProcessAsync(
        Guid id,
        string operation,
        float[] matrix,
        WorkbenchDbContext db,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var record = await db.Images
            .AsNoTracking()
            .SingleOrDefaultAsync(
                image => image.Id == id,
                cancellationToken);

        if (record is null ||
            !File.Exists(record.StoragePath))
        {
            return Results.NotFound("Original image not found.");
        }

        var outputDirectory = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "processed");

        Directory.CreateDirectory(outputDirectory);

        var bytes = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var original =
                SKBitmap.Decode(record.StoragePath);

            if (original is null)
            {
                throw new InvalidOperationException(
                    "Cannot decode the original image.");
            }

            using var output = new SKBitmap(
                original.Width, original.Height);

            using var canvas = new SKCanvas(output);
            using var filter =
                SKColorFilter.CreateColorMatrix(matrix);

            using var paint = new SKPaint
            {
                ColorFilter = filter
            };

            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(original, 0, 0, paint);
            canvas.Flush();

            using var image = SKImage.FromBitmap(output);
            using var encoded = image.Encode(
                SKEncodedImageFormat.Png, 100);

            cancellationToken.ThrowIfCancellationRequested();

            return encoded.ToArray();
        }, cancellationToken);

        var outputName =
            $"{id}_{operation}_{Guid.NewGuid():N}.png";

        var outputPath = Path.Combine(
            outputDirectory, outputName);

        await File.WriteAllBytesAsync(
            outputPath, bytes, cancellationToken);

        return Results.File(bytes, "image/png", outputName);
    }
}
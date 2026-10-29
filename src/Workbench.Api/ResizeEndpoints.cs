using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Workbench.Infrastructure;

namespace Workbench.Api;

public static class ResizeEndpoints
{
    public static void MapResizeEndpoints(
        this WebApplication app)
    {
        app.MapPost("/images/{id:guid}/resize", async (
            Guid id,
            int width,
            WorkbenchDbContext db,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (width < 1 || width > 8000)
            {
                return Results.BadRequest(
                    "Width must be between 1 and 8000 pixels.");
            }

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

            if (record.Width < 1 || record.Height < 1)
            {
                return Results.BadRequest(
                    "Original image dimensions are invalid.");
            }

            var calculatedHeight = Math.Max(
                1d,
                Math.Round(
                    (double)record.Height * width / record.Width,
                    MidpointRounding.AwayFromZero));

            if (calculatedHeight > 8000 ||
                width * calculatedHeight > 25_000_000)
            {
                return Results.BadRequest(
                    "Output exceeds the supported size limit.");
            }

            var height = (int)calculatedHeight;

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

                using var output = new SKBitmap(width, height);
                using var canvas = new SKCanvas(output);

                canvas.Clear(SKColors.Transparent);

                using var image = SKImage.FromBitmap(original);

                var sampling = new SKSamplingOptions(
                    SKFilterMode.Linear,
                    SKMipmapMode.None);

                canvas.DrawImage(
                    image,
                    new SKRect(0, 0, width, height),
                    sampling);

                canvas.Flush();

                cancellationToken.ThrowIfCancellationRequested();

                using var resized = SKImage.FromBitmap(output);

                using var encoded = resized.Encode(
                    SKEncodedImageFormat.Png, 100);

                return encoded.ToArray();
            }, cancellationToken);

            var outputDirectory = Path.Combine(
                environment.ContentRootPath,
                "App_Data",
                "processed");

            Directory.CreateDirectory(outputDirectory);

            var outputName =
                $"{id}_resized_{width}x{height}_{Guid.NewGuid():N}.png";

            var outputPath = Path.Combine(
                outputDirectory, outputName);

            await File.WriteAllBytesAsync(
                outputPath, bytes, cancellationToken);

            return Results.File(bytes, "image/png", outputName);
        });
    }
}
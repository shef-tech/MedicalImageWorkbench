using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Workbench.Infrastructure;

namespace Workbench.Api;

public static class ThresholdEndpoints
{
    public static void MapThresholdEndpoints(
        this WebApplication app)
    {
        app.MapPost("/images/{id:guid}/threshold", async (
            Guid id,
            int cutoff,
            WorkbenchDbContext db,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (cutoff < 0 || cutoff > 255)
            {
                return Results.BadRequest(
                    "Cutoff must be between 0 and 255.");
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

            var bytes = await Task.Run(() =>
            {
                using var original =
                    SKBitmap.Decode(record.StoragePath);

                if (original is null)
                {
                    throw new InvalidOperationException(
                        "Cannot decode the original image.");
                }

                using var output = new SKBitmap(
                    original.Width, original.Height);

                for (var y = 0; y < original.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    for (var x = 0; x < original.Width; x++)
                    {
                        var pixel = original.GetPixel(x, y);

                        var intensity =
                            0.2126 * pixel.Red +
                            0.7152 * pixel.Green +
                            0.0722 * pixel.Blue;

                        byte value = intensity >= cutoff
                            ? (byte)255
                            : (byte)0;

                        output.SetPixel(
                            x, y,
                            new SKColor(
                                value, value, value, pixel.Alpha));
                    }
                }

                using var image = SKImage.FromBitmap(output);

                using var encoded = image.Encode(
                    SKEncodedImageFormat.Png, 100);

                return encoded.ToArray();
            }, cancellationToken);

            var outputDirectory = Path.Combine(
                environment.ContentRootPath,
                "App_Data",
                "processed");

            Directory.CreateDirectory(outputDirectory);

            var outputName =
                $"{id}_threshold_{cutoff}_{Guid.NewGuid():N}.png";

            var outputPath = Path.Combine(
                outputDirectory, outputName);

            await File.WriteAllBytesAsync(
                outputPath, bytes, cancellationToken);

            return Results.File(bytes, "image/png", outputName);
        });
    }
}
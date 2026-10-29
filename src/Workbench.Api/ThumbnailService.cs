using SkiaSharp;

namespace Workbench.Api;

public sealed class ThumbnailService
{
    private readonly string _thumbnailDirectory;

    public ThumbnailService(IWebHostEnvironment environment)
    {
        _thumbnailDirectory = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "thumbnails");

        Directory.CreateDirectory(_thumbnailDirectory);
    }

    public async Task<string> CreateAsync(
        Guid imageId,
        string originalPath,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(
            _thumbnailDirectory,
            $"{imageId}.png");

        if (File.Exists(outputPath))
            return outputPath;

        var bytes = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var original = SKBitmap.Decode(originalPath);

            if (original is null)
            {
                throw new InvalidOperationException(
                    "Cannot decode the image for its thumbnail.");
            }

            const int maximumDimension = 160;

            var scale = Math.Min(
                1d,
                (double)maximumDimension /
                Math.Max(original.Width, original.Height));

            var width = Math.Max(
                1, (int)Math.Round(original.Width * scale));

            var height = Math.Max(
                1, (int)Math.Round(original.Height * scale));

            using var output = new SKBitmap(width, height);
            using var canvas = new SKCanvas(output);
            using var image = SKImage.FromBitmap(original);

            canvas.Clear(SKColors.Transparent);

            canvas.DrawImage(
                image,
                new SKRect(0, 0, width, height),
                new SKSamplingOptions(
                    SKFilterMode.Linear,
                    SKMipmapMode.None));

            canvas.Flush();

            cancellationToken.ThrowIfCancellationRequested();

            using var thumbnail = SKImage.FromBitmap(output);

            using var encoded = thumbnail.Encode(
                SKEncodedImageFormat.Png, 100);

            return encoded.ToArray();
        }, cancellationToken);

        // Separate temporary files allow simultaneous requests
        // without sharing a partially written output.
        var temporaryPath = Path.Combine(
            _thumbnailDirectory,
            $"{imageId}_{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath, bytes, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(
                temporaryPath,
                outputPath,
                overwrite: true);

            return outputPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
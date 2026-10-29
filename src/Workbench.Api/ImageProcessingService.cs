using SkiaSharp;
using Workbench.Core;

namespace Workbench.Api;

public record ProcessingSettings(
    float Brightness = 0,
    float Contrast = 1,
    int Cutoff = 128,
    int Width = 300);

public interface IImageProcessingService
{
    Task<string> ProcessAsync(
        Guid jobId,
        string inputPath,
        ProcessingOperation operation,
        ProcessingSettings settings,
        CancellationToken cancellationToken);
}

public sealed class ImageProcessingService
    : IImageProcessingService
{
    private readonly string _outputDirectory;

    public ImageProcessingService(
        IWebHostEnvironment environment)
    {
        _outputDirectory = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "processed");

        Directory.CreateDirectory(_outputDirectory);
    }

    public async Task<string> ProcessAsync(
        Guid jobId,
        string inputPath,
        ProcessingOperation operation,
        ProcessingSettings settings,
        CancellationToken cancellationToken)
    {
        var bytes = await Task.Run(
            () => Transform(
                inputPath,
                operation,
                settings,
                cancellationToken),
            cancellationToken);

        var outputPath = Path.Combine(
            _outputDirectory,
            $"{jobId}_{operation}.png");

        var temporaryPath = outputPath + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                bytes,
                cancellationToken);

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

    private static byte[] Transform(
        string inputPath,
        ProcessingOperation operation,
        ProcessingSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var original = SKBitmap.Decode(inputPath);

        if (original is null)
        {
            throw new InvalidOperationException(
                "Cannot decode the original image.");
        }

        var width = original.Width;
        var height = original.Height;

        if (operation == ProcessingOperation.Resize)
        {
            if (settings.Width < 1 || settings.Width > 8000)
            {
                throw new ArgumentException(
                    "Width must be between 1 and 8000.");
            }

            width = settings.Width;

            var calculatedHeight = Math.Max(
                1d,
                Math.Round(
                    (double)original.Height *
                    width / original.Width,
                    MidpointRounding.AwayFromZero));

            if (calculatedHeight > 8000 ||
                width * calculatedHeight > 25_000_000)
            {
                throw new ArgumentException(
                    "Output exceeds the supported size limit.");
            }

            height = (int)calculatedHeight;
        }

        using var output = new SKBitmap(width, height);

        if (operation == ProcessingOperation.Threshold)
        {
            if (settings.Cutoff < 0 || settings.Cutoff > 255)
            {
                throw new ArgumentException(
                    "Cutoff must be between 0 and 255.");
            }

            // Work on arrays rather than calling GetPixel/SetPixel
            // separately for every pixel.
            var pixels = original.Pixels;

            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var pixel = pixels[index];

                    var intensity =
                        0.2126 * pixel.Red +
                        0.7152 * pixel.Green +
                        0.0722 * pixel.Blue;

                    byte value = intensity >= settings.Cutoff
                        ? (byte)255
                        : (byte)0;

                    pixels[index] = new SKColor(
                        value, value, value, pixel.Alpha);
                }
            }

            output.Pixels = pixels;
        }
        else
        {
            using var canvas = new SKCanvas(output);
            using var image = SKImage.FromBitmap(original);
            using var paint = new SKPaint();

            SKColorFilter? filter = null;

            try
            {
                switch (operation)
                {
                    case ProcessingOperation.Grayscale:
                        filter = SKColorFilter.CreateColorMatrix(
                            new float[]
                            {
                                0.2126f, 0.7152f, 0.0722f, 0, 0,
                                0.2126f, 0.7152f, 0.0722f, 0, 0,
                                0.2126f, 0.7152f, 0.0722f, 0, 0,
                                0, 0, 0, 1, 0
                            });
                        break;

                    case ProcessingOperation.Adjust:
                        if (!float.IsFinite(settings.Brightness) ||
                            settings.Brightness < -100 ||
                            settings.Brightness > 100 ||
                            !float.IsFinite(settings.Contrast) ||
                            settings.Contrast < 0.1f ||
                            settings.Contrast > 3)
                        {
                            throw new ArgumentException(
                                "Invalid brightness or contrast.");
                        }

                        var contrast = settings.Contrast;

                        // Normalized offset for the installed
                        // SkiaSharp 4.154.0 version.
                        var offset =
                            (128f * (1 - contrast) +
                             settings.Brightness) / 255f;

                        filter = SKColorFilter.CreateColorMatrix(
                            new float[]
                            {
                                contrast, 0, 0, 0, offset,
                                0, contrast, 0, 0, offset,
                                0, 0, contrast, 0, offset,
                                0, 0, 0, 1, 0
                            });
                        break;

                    case ProcessingOperation.Resize:
                        break;

                    default:
                        throw new ArgumentException(
                            "Unsupported operation.");
                }

                paint.ColorFilter = filter;

                canvas.Clear(SKColors.Transparent);

                canvas.DrawImage(
                    image,
                    new SKRect(0, 0, width, height),
                    new SKSamplingOptions(
                        SKFilterMode.Linear,
                        SKMipmapMode.None),
                    paint);

                canvas.Flush();
            }
            finally
            {
                paint.ColorFilter = null;
                filter?.Dispose();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var result = SKImage.FromBitmap(output);
        using var encoded = result.Encode(
            SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }
}
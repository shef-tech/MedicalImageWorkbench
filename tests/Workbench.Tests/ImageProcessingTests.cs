using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using SkiaSharp;
using Workbench.Api;
using Workbench.Core;
using Xunit;

namespace Workbench.Tests;

public sealed class ImageProcessingTests : IDisposable
{
    private readonly string _root;
    private readonly string _inputPath;
    private readonly ImageProcessingService _service;

    public ImageProcessingTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "WorkbenchProcessingTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);

        _inputPath = Path.Combine(_root, "input.png");

        using var bitmap = new SKBitmap(4, 2);

        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Green);
        bitmap.SetPixel(2, 0, SKColors.Blue);
        bitmap.SetPixel(3, 0, SKColors.White);

        bitmap.SetPixel(0, 1, new SKColor(40, 40, 40));
        bitmap.SetPixel(1, 1, new SKColor(100, 100, 100));
        bitmap.SetPixel(2, 1, new SKColor(128, 128, 128));
        bitmap.SetPixel(3, 1, SKColors.Black);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Png, 100);

        File.WriteAllBytes(_inputPath, encoded.ToArray());

        _service = new ImageProcessingService(
            new TestWebHostEnvironment(_root));
    }

    [Fact]
    public async Task GrayscaleProducesEqualColorChannels()
    {
        var originalBytes =
            await File.ReadAllBytesAsync(_inputPath);

        var outputPath = await ProcessAsync(
            ProcessingOperation.Grayscale,
            new ProcessingSettings());

        using var output = Decode(outputPath);

        Assert.Equal(4, output.Width);
        Assert.Equal(2, output.Height);

        foreach (var pixel in output.Pixels)
        {
            Assert.InRange(
                Math.Abs((int)pixel.Red - pixel.Green), 0, 1);

            Assert.InRange(
                Math.Abs((int)pixel.Green - pixel.Blue), 0, 1);
        }

        // Check a known color, not just channel equality.
        var redAsGray = output.GetPixel(0, 0);

        Assert.InRange((int)redAsGray.Red, 53, 55);

        var afterProcessing =
            await File.ReadAllBytesAsync(_inputPath);

        Assert.Equal(originalBytes, afterProcessing);
        Assert.NotEqual(_inputPath, outputPath);
    }

    [Fact]
    public async Task NeutralAdjustmentPreservesPixelValues()
    {
        var outputPath = await ProcessAsync(
            ProcessingOperation.Adjust,
            new ProcessingSettings(
                Brightness: 0,
                Contrast: 1));

        using var original = Decode(_inputPath);
        using var output = Decode(outputPath);

        Assert.Equal(original.Width, output.Width);
        Assert.Equal(original.Height, output.Height);

        for (var y = 0; y < original.Height; y++)
        {
            for (var x = 0; x < original.Width; x++)
            {
                Assert.Equal(
                    original.GetPixel(x, y),
                    output.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public async Task BrightnessIncreasesKnownGrayPixel()
    {
        var outputPath = await ProcessAsync(
            ProcessingOperation.Adjust,
            new ProcessingSettings(
                Brightness: 20,
                Contrast: 1));

        using var output = Decode(outputPath);

        // Input pixel (1, 1) has RGB values of 100.
        var pixel = output.GetPixel(1, 1);

        Assert.InRange((int)pixel.Red, 119, 121);
        Assert.InRange((int)pixel.Green, 119, 121);
        Assert.InRange((int)pixel.Blue, 119, 121);
    }

    [Fact]
    public async Task ThresholdSeparatesDarkAndBrightPixels()
    {
        var outputPath = await ProcessAsync(
            ProcessingOperation.Threshold,
            new ProcessingSettings(Cutoff: 128));

        using var output = Decode(outputPath);

        // Gray 100 is below the cutoff.
        Assert.Equal(SKColors.Black, output.GetPixel(1, 1));

        // Gray 128 meets the cutoff.
        Assert.Equal(SKColors.White, output.GetPixel(2, 1));

        foreach (var pixel in output.Pixels)
        {
            Assert.True(
                pixel == SKColors.Black ||
                pixel == SKColors.White);
        }
    }

    [Fact]
    public async Task ResizePreservesAspectRatio()
    {
        var outputPath = await ProcessAsync(
            ProcessingOperation.Resize,
            new ProcessingSettings(Width: 6));

        using var output = Decode(outputPath);

        // Original is 4 × 2, so width 6 gives height 3.
        Assert.Equal(6, output.Width);
        Assert.Equal(3, output.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8001)]
    public async Task InvalidResizeWidthIsRejected(int width)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => ProcessAsync(
                ProcessingOperation.Resize,
                new ProcessingSettings(Width: width)));

        AssertNoOutputFiles();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public async Task InvalidThresholdIsRejected(int cutoff)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => ProcessAsync(
                ProcessingOperation.Threshold,
                new ProcessingSettings(Cutoff: cutoff)));

        AssertNoOutputFiles();
    }

    [Fact]
    public async Task InvalidContrastIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => ProcessAsync(
                ProcessingOperation.Adjust,
                new ProcessingSettings(Contrast: 0)));

        AssertNoOutputFiles();
    }

    [Fact]
    public async Task PreCancelledOperationCreatesNoOutput()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ProcessAsync(
                Guid.NewGuid(),
                _inputPath,
                ProcessingOperation.Grayscale,
                new ProcessingSettings(),
                cancellation.Token));

        AssertNoOutputFiles();
    }

    [Fact]
    public async Task CorruptImageIsRejected()
    {
        var corruptPath = Path.Combine(
            _root, "corrupt.png");

        await File.WriteAllTextAsync(
            corruptPath, "This is not an image.");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ProcessAsync(
                Guid.NewGuid(),
                corruptPath,
                ProcessingOperation.Grayscale,
                new ProcessingSettings(),
                CancellationToken.None));

        AssertNoOutputFiles();
    }

    private Task<string> ProcessAsync(
        ProcessingOperation operation,
        ProcessingSettings settings)
    {
        return _service.ProcessAsync(
            Guid.NewGuid(),
            _inputPath,
            operation,
            settings,
            CancellationToken.None);
    }

    private static SKBitmap Decode(string path)
    {
        return SKBitmap.Decode(path)
            ?? throw new InvalidOperationException(
                $"Cannot decode test image: {path}");
    }

    private void AssertNoOutputFiles()
    {
        var outputDirectory = Path.Combine(
            _root, "App_Data", "processed");

        Assert.Empty(
            Directory.EnumerateFiles(outputDirectory));
    }

    public void Dispose()
    {
        // Delete only this test instance's temporary directory.
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestWebHostEnvironment
        : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = root;
        }

        public string ApplicationName { get; set; } =
            "Workbench.Tests";

        public string EnvironmentName { get; set; } =
            "Testing";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider
        {
            get;
            set;
        } = new NullFileProvider();

        public string WebRootPath { get; set; }

        public IFileProvider WebRootFileProvider
        {
            get;
            set;
        } = new NullFileProvider();
    }
}
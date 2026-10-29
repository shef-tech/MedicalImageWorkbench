using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using SkiaSharp;
using Workbench.Api;
using Workbench.Core;

namespace Workbench.Benchmarks;

public static class Program
{
    private const int ImageCount = 24;
    private const int ImageWidth = 1024;
    private const int ImageHeight = 768;
    private const int Repetitions = 3;

    public record Measurement(
        int Concurrency,
        double Seconds,
        double ImagesPerSecond,
        double PeakWorkingSetMiB,
        int Completed,
        int Failed);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[0] == "--worker")
            {
                var result = await MeasureAsync(
                    args[1],
                    args[2],
                    int.Parse(
                        args[3],
                        CultureInfo.InvariantCulture));

                Console.WriteLine(
                    JsonSerializer.Serialize(result));

                return result.Failed == 0 ? 0 : 1;
            }

            await RunBenchmarkAsync();

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task RunBenchmarkAsync()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "WorkbenchBenchmarks",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var dataset = Path.Combine(
                temporaryRoot,
                "dataset");

            Directory.CreateDirectory(dataset);

            Console.WriteLine(
                $"Generating {ImageCount} synthetic " +
                $"{ImageWidth} × {ImageHeight} PNG images...");

            GenerateDataset(dataset);

            var measurements = new List<Measurement>();

            for (var repetition = 0;
                 repetition < Repetitions;
                 repetition++)
            {
                var order = repetition % 2 == 0
                    ? new[] { 1, 2 }
                    : new[] { 2, 1 };

                foreach (var concurrency in order)
                {
                    Console.WriteLine(
                        $"Run {repetition + 1}/{Repetitions}, " +
                        $"concurrency {concurrency}...");

                    var runRoot = Path.Combine(
                        temporaryRoot,
                        $"run-{repetition}-{concurrency}");

                    Directory.CreateDirectory(runRoot);

                    var result = await RunChildAsync(
                        dataset,
                        runRoot,
                        concurrency);

                    measurements.Add(result);

                    Console.WriteLine(
                        $"  {result.Seconds:F3} seconds, " +
                        $"{result.ImagesPerSecond:F2} images/second, " +
                        $"{result.PeakWorkingSetMiB:F1} MiB peak, " +
                        $"{result.Failed} failed");
                }
            }

            WriteReports(measurements);
        }
        finally
        {
            // Only this benchmark's unique temporary directory.
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(
                    temporaryRoot,
                    recursive: true);
            }
        }
    }

    private static void GenerateDataset(string directory)
    {
        for (var imageIndex = 0;
             imageIndex < ImageCount;
             imageIndex++)
        {
            var random = new Random(
                20261029 + imageIndex);

            using var bitmap = new SKBitmap(
                ImageWidth,
                ImageHeight);

            var pixels = new SKColor[
                ImageWidth * ImageHeight];

            for (var index = 0;
                 index < pixels.Length;
                 index++)
            {
                pixels[index] = new SKColor(
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    255);
            }

            bitmap.Pixels = pixels;

            using var image =
                SKImage.FromBitmap(bitmap);

            using var encoded = image.Encode(
                SKEncodedImageFormat.Png,
                100);

            File.WriteAllBytes(
                Path.Combine(
                    directory,
                    $"synthetic-{imageIndex:D3}.png"),
                encoded.ToArray());
        }
    }

    private static async Task<Measurement> RunChildAsync(
        string dataset,
        string runRoot,
        int concurrency)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Cannot determine the executable path.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Support execution through either the apphost
        // or "dotnet assembly.dll".
        if (string.Equals(
            Path.GetFileNameWithoutExtension(executable),
            "dotnet",
            StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(
                Assembly.GetExecutingAssembly().Location);
        }

        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add(dataset);
        startInfo.ArgumentList.Add(runRoot);

        startInfo.ArgumentList.Add(
            concurrency.ToString(
                CultureInfo.InvariantCulture));

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        var standardOutput =
            process.StandardOutput.ReadToEndAsync();

        var standardError =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await standardOutput;
        var error = await standardError;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Benchmark child failed.\n{error}\n{output}");
        }

        return JsonSerializer.Deserialize<Measurement>(
            output.Trim())
            ?? throw new InvalidOperationException(
                "The benchmark returned no measurement.");
    }

    private static async Task<Measurement> MeasureAsync(
        string dataset,
        string runRoot,
        int concurrency)
    {
        var files = Directory.GetFiles(
                dataset,
                "*.png")
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                "Dataset is empty.");
        }

        var processor = new ImageProcessingService(
            new BenchmarkEnvironment(runRoot));

        var settings = new ProcessingSettings(
            Cutoff: 128);

        // Exclude one warm-up operation from elapsed time.
        var warmupOutput = await processor.ProcessAsync(
            Guid.NewGuid(),
            files[0],
            ProcessingOperation.Threshold,
            settings,
            CancellationToken.None);

        File.Delete(warmupOutput);

        var completed = 0;
        var errors = new ConcurrentQueue<string>();

        var stopwatch = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency
            },
            async (file, cancellationToken) =>
            {
                try
                {
                    await processor.ProcessAsync(
                        Guid.NewGuid(),
                        file,
                        ProcessingOperation.Threshold,
                        settings,
                        cancellationToken);

                    Interlocked.Increment(
                        ref completed);
                }
                catch (Exception exception)
                {
                    errors.Enqueue(
                        exception.Message);
                }
            });

        stopwatch.Stop();

        foreach (var error in errors)
        {
            Console.Error.WriteLine(error);
        }

        using var process =
            Process.GetCurrentProcess();

        process.Refresh();

        var seconds =
            stopwatch.Elapsed.TotalSeconds;

        return new Measurement(
            concurrency,
            seconds,
            completed / seconds,
            process.PeakWorkingSet64 /
                (1024d * 1024d),
            completed,
            errors.Count);
    }

    private static double Median(
        IEnumerable<double> values)
    {
        var ordered = values
            .OrderBy(value => value)
            .ToArray();

        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] +
               ordered[middle]) / 2
            : ordered[middle];
    }

    private static void WriteReports(
        List<Measurement> measurements)
    {
        var reportDirectory = Path.Combine(
            Directory.GetCurrentDirectory(),
            "docs",
            "benchmarks");

        Directory.CreateDirectory(reportDirectory);

        var csvPath = Path.Combine(
            reportDirectory,
            "results.csv");

        using (var writer = new StreamWriter(csvPath))
        {
            writer.WriteLine(
                "Concurrency,Seconds,ImagesPerSecond," +
                "PeakWorkingSetMiB,Completed,Failed");

            foreach (var result in measurements)
            {
                var row = string.Join(
                    ",",
                    result.Concurrency.ToString(
                        CultureInfo.InvariantCulture),
                    result.Seconds.ToString(
                        "F6",
                        CultureInfo.InvariantCulture),
                    result.ImagesPerSecond.ToString(
                        "F6",
                        CultureInfo.InvariantCulture),
                    result.PeakWorkingSetMiB.ToString(
                        "F3",
                        CultureInfo.InvariantCulture),
                    result.Completed.ToString(
                        CultureInfo.InvariantCulture),
                    result.Failed.ToString(
                        CultureInfo.InvariantCulture));

                writer.WriteLine(row);
            }
        }

        var sequential = measurements
            .Where(result =>
                result.Concurrency == 1)
            .ToArray();

        var parallel = measurements
            .Where(result =>
                result.Concurrency == 2)
            .ToArray();

        var sequentialTime = Median(
            sequential.Select(result =>
                result.Seconds));

        var parallelTime = Median(
            parallel.Select(result =>
                result.Seconds));

        var reduction =
            100 * (sequentialTime - parallelTime) /
            sequentialTime;

        var speedup =
            sequentialTime / parallelTime;

        var report = FormattableString.Invariant($"""
            # Image Processing Benchmark

            Generated at: {DateTimeOffset.UtcNow:O}

            ## Configuration

            - Runtime: {RuntimeInformation.FrameworkDescription}
            - Operating system: {RuntimeInformation.OSDescription}
            - Architecture: {RuntimeInformation.ProcessArchitecture}
            - Logical processors: {Environment.ProcessorCount}
            - Dataset: {ImageCount} synthetic PNG images
            - Image dimensions: {ImageWidth} × {ImageHeight}
            - Generator: seeded System.Random, opaque random RGB pixels
            - Operation: threshold at 128
            - Repetitions: {Repetitions} per concurrency
            - Each measurement uses a fresh child process
            - Run ordering alternates between concurrency 1 and 2

            ## Results

            | Metric | Concurrency 1 | Concurrency 2 |
            |---|---:|---:|
            | Median elapsed seconds | {sequentialTime:F3} | {parallelTime:F3} |
            | Median images/second | {Median(sequential.Select(x => x.ImagesPerSecond)):F2} | {Median(parallel.Select(x => x.ImagesPerSecond)):F2} |
            | Median process peak working set, MiB | {Median(sequential.Select(x => x.PeakWorkingSetMiB)):F1} | {Median(parallel.Select(x => x.PeakWorkingSetMiB)):F1} |
            | Total failed operations | {sequential.Sum(x => x.Failed)} | {parallel.Sum(x => x.Failed)} |

            Elapsed-time reduction: {reduction:F2}%.

            Speedup ratio: {speedup:F2}×.

            A negative time reduction means concurrency 2 was slower.

            ## Measurement limits

            Run this benchmark using Release configuration.

            Timed work includes file decoding, thresholding, PNG encoding,
            and output file writing. It excludes dataset generation,
            process startup, warm-up, HTTP, job queues, and SQLite.

            Peak working set covers the entire child-process lifetime,
            including runtime startup and warm-up. It is not incremental
            memory allocated only by the timed operation.

            File-system caches are not cleared. Results depend on this
            machine, workload, image sizes, and storage. Synthetic images
            do not represent every medical research dataset.

            Raw measurements are in results.csv.
            """);

        var reportPath = Path.Combine(
            reportDirectory,
            "latest.md");

        File.WriteAllText(
            reportPath,
            report);

        Console.WriteLine();
        Console.WriteLine(report);
        Console.WriteLine();
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Raw results: {csvPath}");
    }

    private sealed class BenchmarkEnvironment
        : IWebHostEnvironment
    {
        public BenchmarkEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = root;
        }

        public string ApplicationName { get; set; } =
            "Workbench.Benchmarks";

        public string EnvironmentName { get; set; } =
            "Benchmark";

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
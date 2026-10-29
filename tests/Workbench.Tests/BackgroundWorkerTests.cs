using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workbench.Api;
using Workbench.Core;
using Workbench.Infrastructure;
using Xunit;

namespace Workbench.Tests;

public class BackgroundWorkerTests
{
    [Fact]
    public async Task RunningJobReceivesCancellation()
    {
        await using var harness =
            await WorkerHarness.CreateAsync();

        await harness.Worker.StartAsync(
            CancellationToken.None);

        // Wait until processing has actually started.
        await harness.Processor.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(10));

        await using (var db = harness.Factory.CreateDbContext())
        {
            var running = await db.ProcessingJobs
                .SingleAsync(job => job.Id == harness.JobId);

            Assert.Equal(
                ProcessingJobStatus.Running,
                running.Status);

            // Reproduce the cancellation endpoint's sequence:
            // persist cancellation, then signal the running job.
            var changed = await db.ProcessingJobs
                .Where(job =>
                    job.Id == harness.JobId &&
                    job.Status == ProcessingJobStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            job => job.Status,
                            ProcessingJobStatus.Cancelled)
                        .SetProperty(
                            job => job.CompletedAt,
                            (DateTime?)DateTime.UtcNow));

            Assert.Equal(1, changed);
        }

        harness.Registry.Cancel(harness.JobId);

        await harness.Processor.CancellationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(10));

        await harness.StopAsync();

        await using (var db = harness.Factory.CreateDbContext())
        {
            var saved = await db.ProcessingJobs
                .SingleAsync(job => job.Id == harness.JobId);

            Assert.Equal(
                ProcessingJobStatus.Cancelled,
                saved.Status);

            Assert.Null(saved.OutputPath);
        }

        Assert.False(File.Exists(harness.Processor.OutputPath));
    }

    [Fact]
    public async Task CompletedJobKeepsItsOutputAfterCancellationAttempt()
    {
        await using var harness =
            await WorkerHarness.CreateAsync();

        await harness.Worker.StartAsync(
            CancellationToken.None);

        await harness.Processor.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(10));

        harness.Processor.AllowCompletion.TrySetResult(true);

        await WaitForStatusAsync(
            harness.Factory,
            harness.JobId,
            ProcessingJobStatus.Completed);

        await using (var db = harness.Factory.CreateDbContext())
        {
            // The endpoint only cancels Queued or Running jobs.
            var changed = await db.ProcessingJobs
                .Where(job =>
                    job.Id == harness.JobId &&
                    (job.Status == ProcessingJobStatus.Queued ||
                     job.Status == ProcessingJobStatus.Running))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        job => job.Status,
                        ProcessingJobStatus.Cancelled));

            Assert.Equal(0, changed);
        }

        harness.Registry.Cancel(harness.JobId);

        await harness.StopAsync();

        await using (var db = harness.Factory.CreateDbContext())
        {
            var saved = await db.ProcessingJobs
                .SingleAsync(job => job.Id == harness.JobId);

            Assert.Equal(
                ProcessingJobStatus.Completed,
                saved.Status);

            Assert.Equal(
                harness.Processor.OutputPath,
                saved.OutputPath);
        }

        Assert.True(File.Exists(harness.Processor.OutputPath));
    }

    private static async Task WaitForStatusAsync(
        TestContextFactory factory,
        Guid jobId,
        ProcessingJobStatus expected)
    {
        using var timeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            while (true)
            {
                await using var db =
                    factory.CreateDbContext();

                var status = await db.ProcessingJobs
                    .Where(job => job.Id == jobId)
                    .Select(job => job.Status)
                    .SingleAsync(timeout.Token);

                if (status == expected)
                    return;

                if (status == ProcessingJobStatus.Failed)
                {
                    throw new InvalidOperationException(
                        "The background test job failed.");
                }

                await Task.Delay(20, timeout.Token);
            }
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Job did not reach {expected} within 10 seconds.");
        }
    }

    private sealed class ControlledProcessor
        : IImageProcessingService
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> AllowCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationObserved
        {
            get;
        } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string OutputPath { get; }

        public ControlledProcessor(string root)
        {
            OutputPath = Path.Combine(
                root, "controlled-output.png");
        }

        public async Task<string> ProcessAsync(
            Guid jobId,
            string inputPath,
            ProcessingOperation operation,
            ProcessingSettings settings,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);

            try
            {
                await AllowCompletion.Task.WaitAsync(
                    cancellationToken);

                await File.WriteAllTextAsync(
                    OutputPath,
                    "Controlled test output",
                    cancellationToken);

                return OutputPath;
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult(true);
                throw;
            }
        }
    }

    private sealed class TestContextFactory
        : IDbContextFactory<WorkbenchDbContext>
    {
        private readonly DbContextOptions<WorkbenchDbContext>
            _options;

        public TestContextFactory(string databasePath)
        {
            var connectionString =
                $"Data Source={databasePath};Pooling=False";

            _options =
                new DbContextOptionsBuilder<WorkbenchDbContext>()
                    .UseSqlite(connectionString)
                    .Options;
        }

        public WorkbenchDbContext CreateDbContext()
        {
            return new WorkbenchDbContext(_options);
        }

        public Task<WorkbenchDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class WorkerHarness : IAsyncDisposable
    {
        private readonly string _root;
        private bool _stopped;

        public Guid JobId { get; }

        public TestContextFactory Factory { get; }
        public ControlledProcessor Processor { get; }
        public JobCancellationRegistry Registry { get; }
        public ProcessingWorker Worker { get; }

        private WorkerHarness(
            string root,
            Guid jobId,
            TestContextFactory factory,
            ControlledProcessor processor,
            JobCancellationRegistry registry,
            ProcessingWorker worker)
        {
            _root = root;
            JobId = jobId;
            Factory = factory;
            Processor = processor;
            Registry = registry;
            Worker = worker;
        }

        public static async Task<WorkerHarness> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "WorkbenchWorkerTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(root);

            var inputPath = Path.Combine(root, "input.png");

            // The controlled processor does not decode this file.
            await File.WriteAllTextAsync(
                inputPath, "Controlled test input");

            var factory = new TestContextFactory(
                Path.Combine(root, "tests.db"));

            var image = new ImageRecord
            {
                OriginalFileName = "input.png",
                StoragePath = inputPath,
                ContentHash = Guid.NewGuid().ToString("N"),
                FileFormat = "Png",
                Width = 10,
                Height = 10,
                FileSize = 21
            };

            var job = new ProcessingJob
            {
                BatchId = Guid.NewGuid(),
                ImageId = image.Id,
                Operation = ProcessingOperation.Grayscale
            };

            await using (var db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();

                db.Images.Add(image);
                db.ProcessingJobs.Add(job);

                await db.SaveChangesAsync();
            }

            var queue = new BackgroundJobQueue();
            var registry = new JobCancellationRegistry();
            var processor = new ControlledProcessor(root);

            var worker = new ProcessingWorker(
                queue,
                factory,
                processor,
                registry,
                NullLogger<ProcessingWorker>.Instance);

            // Worker startup recovers and enqueues the saved job.
            return new WorkerHarness(
                root,
                job.Id,
                factory,
                processor,
                registry,
                worker);
        }

        public async Task StopAsync()
        {
            if (_stopped)
                return;

            using var timeout =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));

            await Worker.StopAsync(timeout.Token);

            _stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync();
            }
            finally
            {
                Worker.Dispose();

                // Only this test's unique temporary folder.
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
        }
    }
}
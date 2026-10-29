using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workbench.Core;
using Workbench.Infrastructure;

namespace Workbench.Api;

public sealed class ProcessingWorker : BackgroundService
{
    private const int WorkerCount = 2;

    private readonly BackgroundJobQueue _queue;
    private readonly IDbContextFactory<WorkbenchDbContext> _factory;
    private readonly IImageProcessingService _processor;
    private readonly JobCancellationRegistry _cancellation;
    private readonly ILogger<ProcessingWorker> _logger;

    public ProcessingWorker(
        BackgroundJobQueue queue,
        IDbContextFactory<WorkbenchDbContext> factory,
        IImageProcessingService processor,
        JobCancellationRegistry cancellation,
        ILogger<ProcessingWorker> logger)
    {
        _queue = queue;
        _factory = factory;
        _processor = processor;
        _cancellation = cancellation;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            var pendingIds =
                await RecoverJobsAsync(stoppingToken);

            var consumers = Enumerable.Range(1, WorkerCount)
                .Select(number =>
                    ConsumeAsync(number, stoppingToken))
                .ToArray();

            foreach (var id in pendingIds)
                await _queue.EnqueueAsync(id, stoppingToken);

            await Task.WhenAll(consumers);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Processing workers stopped.");
        }
    }

    private async Task<List<Guid>> RecoverJobsAsync(
        CancellationToken cancellationToken)
    {
        await using var db =
            await _factory.CreateDbContextAsync(
                cancellationToken);

        await db.ProcessingJobs
            .Where(job =>
                job.Status == ProcessingJobStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        job => job.Status,
                        ProcessingJobStatus.Queued)
                    .SetProperty(
                        job => job.StartedAt,
                        (DateTime?)null)
                    .SetProperty(
                        job => job.CompletedAt,
                        (DateTime?)null)
                    .SetProperty(
                        job => job.ErrorMessage,
                        (string?)null),
                cancellationToken);

        var pendingIds = await db.ProcessingJobs
            .AsNoTracking()
            .Where(job =>
                job.Status == ProcessingJobStatus.Queued)
            .OrderBy(job => job.CreatedAt)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Recovered {Count} queued job(s).",
            pendingIds.Count);

        return pendingIds;
    }

    private async Task ConsumeAsync(
        int workerNumber,
        CancellationToken stoppingToken)
    {
        await foreach (
            var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunJobAsync(
                    jobId, workerNumber, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Worker {Worker} could not handle job {JobId}.",
                    workerNumber,
                    jobId);
            }
        }
    }

    private async Task RunJobAsync(
        Guid jobId,
        int workerNumber,
        CancellationToken stoppingToken)
    {
        // Register before claiming so a cancellation request
        // cannot miss a newly started job.
        var jobToken = _cancellation.Register(
            jobId, stoppingToken);

        string? outputPath = null;
        var outputCommitted = false;

        try
        {
            await using var db =
                await _factory.CreateDbContextAsync(stoppingToken);

            var claimed = await db.ProcessingJobs
                .Where(job =>
                    job.Id == jobId &&
                    job.Status == ProcessingJobStatus.Queued)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            job => job.Status,
                            ProcessingJobStatus.Running)
                        .SetProperty(
                            job => job.StartedAt,
                            (DateTime?)DateTime.UtcNow),
                    jobToken);

            if (claimed == 0)
                return;

            var job = await db.ProcessingJobs
                .AsNoTracking()
                .SingleAsync(
                    item => item.Id == jobId,
                    jobToken);

            try
            {
                var image = await db.Images
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.Id == job.ImageId,
                        jobToken);

                if (image is null ||
                    !File.Exists(image.StoragePath))
                {
                    throw new FileNotFoundException(
                        "The original image is missing.");
                }

                var settings =
                    JsonSerializer.Deserialize<ProcessingSettings>(
                        job.ParametersJson)
                    ?? new ProcessingSettings();

                _logger.LogInformation(
                    "Worker {Worker} started job {JobId}: {Operation}.",
                    workerNumber,
                    jobId,
                    job.Operation);

                outputPath = await _processor.ProcessAsync(
                    jobId,
                    image.StoragePath,
                    job.Operation,
                    settings,
                    jobToken);

                jobToken.ThrowIfCancellationRequested();

                // A cancellation that already changed the status
                // cannot be overwritten by completion.
                var updated = await db.ProcessingJobs
                    .Where(item =>
                        item.Id == jobId &&
                        item.Status == ProcessingJobStatus.Running)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                item => item.Status,
                                ProcessingJobStatus.Completed)
                            .SetProperty(
                                item => item.OutputPath,
                                outputPath)
                            .SetProperty(
                                item => item.CompletedAt,
                                (DateTime?)DateTime.UtcNow)
                            .SetProperty(
                                item => item.ErrorMessage,
                                (string?)null),
                        jobToken);

                outputCommitted = updated == 1;

                if (outputCommitted)
                {
                    _logger.LogInformation(
                        "Job {JobId} completed.", jobId);
                }
            }
            catch (OperationCanceledException)
                when (jobToken.IsCancellationRequested)
            {
                // User cancellation is already persisted by the
                // endpoint. Shutdown-interrupted jobs remain
                // Running and are recovered on restart.
                _logger.LogInformation(
                    "Job {JobId} interrupted or cancelled.",
                    jobId);

                if (stoppingToken.IsCancellationRequested)
                    throw;
            }
            catch (Exception exception)
            {
                await db.ProcessingJobs
                    .Where(item =>
                        item.Id == jobId &&
                        item.Status == ProcessingJobStatus.Running)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                item => item.Status,
                                ProcessingJobStatus.Failed)
                            .SetProperty(
                                item => item.CompletedAt,
                                (DateTime?)DateTime.UtcNow)
                            .SetProperty(
                                item => item.ErrorMessage,
                                "Processing failed. See backend logs."),
                        stoppingToken);

                _logger.LogError(
                    exception,
                    "Job {JobId} failed.",
                    jobId);
            }
        }
        finally
        {
            _cancellation.Release(jobId);

            // Discard an output if its job never committed
            // successful completion.
            if (!outputCommitted &&
                outputPath is not null &&
                File.Exists(outputPath))
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Could not clean up output for job {JobId}.",
                        jobId);
                }
            }
        }
    }
}
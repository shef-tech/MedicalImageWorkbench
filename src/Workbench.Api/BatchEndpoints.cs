using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workbench.Core;
using Workbench.Infrastructure;

namespace Workbench.Api;

public record StartBatchInput(
    Guid[]? ImageIds,
    string? Operation,
    ProcessingSettings? Settings);

public static class BatchEndpoints
{
    public static void MapBatchEndpoints(
        this WebApplication app)
    {
        app.MapPost("/batches", async (
            StartBatchInput input,
            WorkbenchDbContext db,
            BackgroundJobQueue queue,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
        {
            var imageIds = input.ImageIds?
                .Distinct()
                .ToArray() ?? Array.Empty<Guid>();

            if (imageIds.Length < 1 || imageIds.Length > 100)
            {
                return Results.BadRequest(
                    "Select between 1 and 100 distinct images.");
            }

            if (!Enum.TryParse<ProcessingOperation>(
                    input.Operation,
                    ignoreCase: true,
                    out var operation) ||
                !Enum.IsDefined(operation))
            {
                return Results.BadRequest(
                    "Operation must be Grayscale, Adjust, " +
                    "Threshold, or Resize.");
            }

            var settings =
                input.Settings ?? new ProcessingSettings();

            if (operation == ProcessingOperation.Adjust &&
                (!float.IsFinite(settings.Brightness) ||
                 settings.Brightness < -100 ||
                 settings.Brightness > 100 ||
                 !float.IsFinite(settings.Contrast) ||
                 settings.Contrast < 0.1f ||
                 settings.Contrast > 3))
            {
                return Results.BadRequest(
                    "Invalid brightness or contrast.");
            }

            if (operation == ProcessingOperation.Threshold &&
                (settings.Cutoff < 0 || settings.Cutoff > 255))
            {
                return Results.BadRequest(
                    "Cutoff must be between 0 and 255.");
            }

            if (operation == ProcessingOperation.Resize &&
                (settings.Width < 1 || settings.Width > 8000))
            {
                return Results.BadRequest(
                    "Width must be between 1 and 8000.");
            }

            var images = await db.Images
                .AsNoTracking()
                .Where(image => imageIds.Contains(image.Id))
                .ToListAsync(cancellationToken);

            if (images.Count != imageIds.Length)
            {
                return Results.BadRequest(
                    "One or more image IDs do not exist.");
            }

            if (operation == ProcessingOperation.Resize)
            {
                foreach (var image in images)
                {
                    if (image.Width <= 0 || image.Height <= 0)
                    {
                        return Results.BadRequest(
                            "An image has invalid dimensions.");
                    }

                    var height = Math.Max(
                        1d,
                        Math.Round(
                            (double)image.Height *
                            settings.Width / image.Width,
                            MidpointRounding.AwayFromZero));

                    if (height > 8000 ||
                        settings.Width * height > 25_000_000)
                    {
                        return Results.BadRequest(
                            "A resized image would exceed " +
                            "the supported size limit.");
                    }
                }
            }

            var batchId = Guid.NewGuid();

            var parameters = JsonSerializer.Serialize(settings);

            var jobs = imageIds.Select(imageId =>
                new ProcessingJob
                {
                    BatchId = batchId,
                    ImageId = imageId,
                    Operation = operation,
                    ParametersJson = parameters
                }).ToArray();

            db.ProcessingJobs.AddRange(jobs);

            await db.SaveChangesAsync(cancellationToken);

            // After persistence, disconnecting the client must not
            // abandon dispatch. Shutdown recovery handles jobs
            // that remain queued if the backend stops.
            foreach (var job in jobs)
            {
                await queue.EnqueueAsync(
                    job.Id,
                    lifetime.ApplicationStopping);
            }

            return Results.Accepted(
                $"/batches/{batchId}",
                new
                {
                    batchId,
                    total = jobs.Length
                });
        });

        app.MapGet("/batches/{batchId:guid}", async (
            Guid batchId,
            WorkbenchDbContext db,
            CancellationToken cancellationToken) =>
        {
            var jobs = await db.ProcessingJobs
                .AsNoTracking()
                .Where(job => job.BatchId == batchId)
                .OrderBy(job => job.CreatedAt)
                .ThenBy(job => job.Id)
                .ToListAsync(cancellationToken);

            if (jobs.Count == 0)
                return Results.NotFound("Batch not found.");

            var completed = jobs.Count(job =>
                job.Status == ProcessingJobStatus.Completed);

            var failed = jobs.Count(job =>
                job.Status == ProcessingJobStatus.Failed);

            var cancelled = jobs.Count(job =>
                job.Status == ProcessingJobStatus.Cancelled);

            var finished = completed + failed + cancelled;

            return Results.Ok(new
            {
                batchId,
                total = jobs.Count,
                completed,
                failed,
                cancelled,
                running = jobs.Count(job =>
                    job.Status == ProcessingJobStatus.Running),
                queued = jobs.Count(job =>
                    job.Status == ProcessingJobStatus.Queued),
                progressPercent =
                    (int)Math.Round(100d * finished / jobs.Count),
                isFinished = finished == jobs.Count,

                jobs = jobs.Select(job => new
                {
                    job.Id,
                    job.ImageId,
                    operation = job.Operation.ToString(),
                    status = job.Status.ToString(),
                    job.CreatedAt,
                    job.StartedAt,
                    job.CompletedAt,
                    job.ErrorMessage,

                    outputUrl =
                        job.Status == ProcessingJobStatus.Completed
                            ? $"/jobs/{job.Id}/output"
                            : null
                })
            });
        });

        app.MapGet("/jobs/{jobId:guid}/output", async (
            Guid jobId,
            WorkbenchDbContext db,
            CancellationToken cancellationToken) =>
        {
            var job = await db.ProcessingJobs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == jobId,
                    cancellationToken);

            if (job is null)
                return Results.NotFound("Job not found.");

            if (job.Status != ProcessingJobStatus.Completed)
            {
                return Results.Conflict(
                    "The job has not completed successfully.");
            }

            if (string.IsNullOrWhiteSpace(job.OutputPath) ||
                !File.Exists(job.OutputPath))
            {
                return Results.NotFound(
                    "The output file is missing.");
            }

            return Results.File(
                job.OutputPath,
                "image/png");
        });
    }
}
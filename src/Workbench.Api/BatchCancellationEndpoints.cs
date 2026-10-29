using Microsoft.EntityFrameworkCore;
using Workbench.Core;
using Workbench.Infrastructure;

namespace Workbench.Api;

public static class BatchCancellationEndpoints
{
    public static void MapBatchCancellationEndpoints(
        this WebApplication app)
    {
        app.MapPost("/batches/{batchId:guid}/cancel", async (
            Guid batchId,
            WorkbenchDbContext db,
            JobCancellationRegistry registry,
            CancellationToken cancellationToken) =>
        {
            var jobs = await db.ProcessingJobs
                .AsNoTracking()
                .Where(job => job.BatchId == batchId)
                .Select(job => new { job.Id })
                .ToListAsync(cancellationToken);

            if (jobs.Count == 0)
                return Results.NotFound("Batch not found.");

            // Persist cancellation first. Workers may complete
            // only while their database status is Running.
            var cancelledCount = await db.ProcessingJobs
                .Where(job =>
                    job.BatchId == batchId &&
                    (job.Status == ProcessingJobStatus.Queued ||
                     job.Status == ProcessingJobStatus.Running))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            job => job.Status,
                            ProcessingJobStatus.Cancelled)
                        .SetProperty(
                            job => job.CompletedAt,
                            (DateTime?)DateTime.UtcNow)
                        .SetProperty(
                            job => job.ErrorMessage,
                            (string?)null),
                    cancellationToken);

            // Signal running work even if the client disconnects
            // after the database update succeeds.
            foreach (var job in jobs)
                registry.Cancel(job.Id);

            return Results.Ok(new
            {
                batchId,
                cancelledCount,
                message =
                    "Cancellation requested. Completed outputs are preserved."
            });
        });
    }
}
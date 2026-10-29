namespace Workbench.Core;

public enum ProcessingJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum ProcessingOperation
{
    Grayscale,
    Adjust,
    Threshold,
    Resize
}

public class ProcessingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }

    public Guid ImageId { get; set; }

    public ProcessingOperation Operation { get; set; }

    public ProcessingJobStatus Status { get; set; } =
        ProcessingJobStatus.Queued;

    // Serialized operation settings.
    public string ParametersJson { get; set; } = "{}";

    public string? OutputPath { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
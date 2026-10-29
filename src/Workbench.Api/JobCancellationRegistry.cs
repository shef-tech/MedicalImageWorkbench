namespace Workbench.Api;

public sealed class JobCancellationRegistry
{
    private readonly object _gate = new();

    private readonly Dictionary<Guid, CancellationTokenSource>
        _sources = new();

    public CancellationToken Register(
        Guid jobId,
        CancellationToken stoppingToken)
    {
        lock (_gate)
        {
            if (_sources.ContainsKey(jobId))
            {
                throw new InvalidOperationException(
                    "This job is already registered.");
            }

            var source =
                CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken);

            _sources.Add(jobId, source);

            return source.Token;
        }
    }

    public void Cancel(Guid jobId)
    {
        lock (_gate)
        {
            if (_sources.TryGetValue(jobId, out var source))
                source.Cancel();
        }
    }

    public void Release(Guid jobId)
    {
        lock (_gate)
        {
            if (_sources.Remove(jobId, out var source))
                source.Dispose();
        }
    }
}
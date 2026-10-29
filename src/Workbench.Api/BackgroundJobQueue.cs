using System.Threading.Channels;

namespace Workbench.Api;

public sealed class BackgroundJobQueue
{
    private readonly Channel<Guid> _channel;

    public BackgroundJobQueue()
    {
        _channel = Channel.CreateBounded<Guid>(
            new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    public ValueTask EnqueueAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid job ID is required.",
                nameof(jobId));
        }

        return _channel.Writer.WriteAsync(
            jobId,
            cancellationToken);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(
        CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(
            cancellationToken);
    }
}
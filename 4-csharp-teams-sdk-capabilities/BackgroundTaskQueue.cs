using System.Threading.Channels;

internal sealed class BackgroundTaskQueue : BackgroundService
{
    // The queue is in memory for clarity; use durable work orchestration for production.
    private readonly Channel<Func<CancellationToken, Task>> queue =
        Channel.CreateUnbounded<Func<CancellationToken, Task>>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

    public ValueTask EnqueueAsync(
        Func<CancellationToken, Task> workItem,
        CancellationToken cancellationToken) =>
        queue.Writer.WriteAsync(workItem, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Func<CancellationToken, Task> workItem in
            queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await workItem(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("[background] work item failed: {0}", exception);
            }
        }
    }
}

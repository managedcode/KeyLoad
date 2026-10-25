using System.Collections.Concurrent;
using KeyLoad.Client;

namespace KeyLoad.ClusterTests.Messaging;

internal sealed class QueueWorkerClusterHandlerLifecycle(bool holdThird)
{
    internal TaskCompletionSource FirstEntered { get; } = NewSignal();
    internal TaskCompletionSource ThirdEntered { get; } = NewSignal();
    internal TaskCompletionSource ReleaseThird { get; } = NewSignal();
    internal TaskCompletionSource ReleaseFirst { get; } = NewSignal();
    internal ConcurrentDictionary<string, QueueWorkerDeliveryContext> Contexts { get; } = new();
    private int entered;
    private int exited;

    internal int Entered => Volatile.Read(ref entered);
    internal int Exited => Volatile.Read(ref exited);

    internal async Task HandleAsync(QueueWorkerDeliveryContext context, CancellationToken token)
    {
        if (!Contexts.TryAdd(context.Delivery.Id, context))
        {
            throw new InvalidOperationException(QueueWorkerClusterProtocol.Missing);
        }
        Interlocked.Increment(ref entered);
        try
        {
            if (context.Delivery.Id == QueueWorkerClusterProtocol.First)
            {
                FirstEntered.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(token);
            }
            if (context.Delivery.Id == QueueWorkerClusterProtocol.Third)
            {
                ThirdEntered.TrySetResult();
                if (holdThird)
                {
                    await ReleaseThird.Task.WaitAsync(token);
                }
            }
        }
        finally
        {
            Interlocked.Increment(ref exited);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

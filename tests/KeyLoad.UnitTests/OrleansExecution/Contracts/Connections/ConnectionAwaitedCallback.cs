using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class ConnectionAwaitedCallback(Exception? failure)
{
    private int invocations;
    internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource AdmissionClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal async ValueTask NotifyAsync(CancellationToken ownerShutdown)
    {
        if (Interlocked.Increment(ref invocations) != 1)
        {
            throw new InvalidOperationException(ConnectionProtocol.DuplicateObservation);
        }
        using var admission = ownerShutdown.Register(() => AdmissionClosed.TrySetResult());
        Arrived.SetResult();
        await Release.Task.ConfigureAwait(true);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

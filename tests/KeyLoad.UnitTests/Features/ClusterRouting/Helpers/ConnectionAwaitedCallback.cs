using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionAwaitedCallback(Exception? failure)
{
    private const int FirstInvocation = 1;
    private int invocations;
    internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal async ValueTask NotifyAsync()
    {
        if (Interlocked.Increment(ref invocations) != FirstInvocation)
        { throw new InvalidOperationException(ConnectionNativeProtocol.DuplicateObservation); }
        Arrived.SetResult();
        await Release.Task.ConfigureAwait(true);
        if (failure is not null)
        { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }
}

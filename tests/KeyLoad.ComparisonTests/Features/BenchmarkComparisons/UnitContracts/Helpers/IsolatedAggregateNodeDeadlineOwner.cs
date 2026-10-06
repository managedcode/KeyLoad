namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodeDeadlineOwner : IDisposable
{
    private readonly IsolatedAggregateNodeFailureSet failures;
    private readonly TimeProvider clock;
    private CancellationTokenSource? source;
    private CancellationTokenSource? timeout;

    internal IsolatedAggregateNodeDeadlineOwner(IsolatedAggregateNodeFailureSet failures, TimeProvider? clock = null)
    {
        this.failures = failures;
        this.clock = clock ?? TimeProvider.System;
    }

    internal CancellationToken Start(TimeSpan runBound, CancellationToken prompt)
    {
        var token = CancellationToken.None;
        Exception? primary = null;
        var initializationFailures = new IsolatedAggregateNodeFailureSet();
        try
        {
            timeout = IsolatedAggregateNodeGuardedInvocation.Invoke(() => new CancellationTokenSource(runBound, clock));
            var active = CancellationTokenSource.CreateLinkedTokenSource(prompt, timeout.Token);
            source = active;
            token = active.Token;
        }
        catch (AggregateException envelope)
        {
            primary = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
        }
        if (primary is not null)
        {
            DisposeSource(initializationFailures);
            initializationFailures.Throw(primary);
        }
        return token;
    }

    internal CancellationTokenSource? TransferToCleanup()
        => Interlocked.Exchange(ref source, null);

    public void Dispose()
    {
        IsolatedAggregateNodeGuardedInvocation.Capture(() => source?.Dispose(), failures.Add);
        source = null;
        IsolatedAggregateNodeGuardedInvocation.Capture(() => timeout?.Dispose(), failures.Add);
        timeout = null;
    }

    private void DisposeSource(IsolatedAggregateNodeFailureSet target)
    {
        IsolatedAggregateNodeGuardedInvocation.Capture(() => source?.Dispose(), target.Add);
        source = null;
        IsolatedAggregateNodeGuardedInvocation.Capture(() => timeout?.Dispose(), target.Add);
        timeout = null;
    }
}

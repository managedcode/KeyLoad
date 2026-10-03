namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodeDeadlineOwner : IDisposable
{
    private readonly IsolatedAggregateNodeFailureSet failures;
    private CancellationTokenSource? source;

    internal IsolatedAggregateNodeDeadlineOwner(IsolatedAggregateNodeFailureSet failures)
        => this.failures = failures;

    internal CancellationToken Start(TimeSpan runBound, CancellationToken prompt)
    {
        var token = CancellationToken.None;
        Exception? primary = null;
        var initializationFailures = new IsolatedAggregateNodeFailureSet();
        try
        {
            var active = CancellationTokenSource.CreateLinkedTokenSource(prompt);
            source = active;
            IsolatedAggregateNodeGuardedInvocation.Invoke(() => active.CancelAfter(runBound));
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
    }

    private void DisposeSource(IsolatedAggregateNodeFailureSet target)
    {
        IsolatedAggregateNodeGuardedInvocation.Capture(() => source?.Dispose(), target.Add);
        source = null;
    }
}

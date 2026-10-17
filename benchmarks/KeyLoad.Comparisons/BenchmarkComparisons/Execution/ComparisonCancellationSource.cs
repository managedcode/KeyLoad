namespace KeyLoad.Comparisons;

/// <summary>Owns native provider timer cancellation and the incoming token registration.</summary>
internal sealed class ComparisonCancellationSource : CancellationTokenSource
{
    private readonly CancellationTokenRegistration incoming;

    internal ComparisonCancellationSource(TimeProvider timeProvider, CancellationToken token)
        : base(Timeout.InfiniteTimeSpan, timeProvider)
    {
        incoming = token.UnsafeRegister(static state => ((ComparisonCancellationSource)state!).Cancel(), this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            incoming.Dispose();
        }
        base.Dispose(disposing);
    }
}

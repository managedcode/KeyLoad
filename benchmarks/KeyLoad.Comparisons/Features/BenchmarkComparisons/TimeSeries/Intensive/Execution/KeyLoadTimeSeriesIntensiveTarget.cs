namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class KeyLoadTimeSeriesIntensiveTarget(KeyLoadTimeSeriesIntensiveContext context)
    : ITimeSeriesIntensiveTarget
{
    private int closed = KeyLoadTimeSeriesIntensiveProtocol.OpenState;
    private int seedOrdinal = KeyLoadTimeSeriesIntensiveProtocol.FirstSeedOrdinal;
    private long maximumValidatedAcknowledgementPosition;

    internal long MaximumValidatedAcknowledgementPosition =>
        Interlocked.Read(ref maximumValidatedAcknowledgementPosition);

    internal KeyLoadTimeSeriesIntensiveContext Context { get; } =
        context ?? throw new ArgumentNullException(nameof(context));

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref closed, KeyLoadTimeSeriesIntensiveProtocol.ClosedState);
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (Volatile.Read(ref closed) != KeyLoadTimeSeriesIntensiveProtocol.OpenState)
        {
            throw new ObjectDisposedException(nameof(KeyLoadTimeSeriesIntensiveTarget),
                KeyLoadTimeSeriesIntensiveProtocol.ClosedTarget);
        }
    }

    private void RetainAcknowledgementPosition(long position)
    {
        var current = Interlocked.Read(ref maximumValidatedAcknowledgementPosition);
        while (position > current)
        {
            var observed = Interlocked.CompareExchange(ref maximumValidatedAcknowledgementPosition, position, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}

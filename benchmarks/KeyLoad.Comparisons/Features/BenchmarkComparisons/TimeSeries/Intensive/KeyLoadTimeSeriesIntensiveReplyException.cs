namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class KeyLoadTimeSeriesIntensiveReplyException : InvalidOperationException
{
    public KeyLoadTimeSeriesIntensiveReplyException()
        : this((long?)null)
    {
    }

    public KeyLoadTimeSeriesIntensiveReplyException(string message)
        : base(KeyLoadTimeSeriesIntensiveProtocol.InvalidReceipt)
    {
        ArgumentNullException.ThrowIfNull(message);
    }

    public KeyLoadTimeSeriesIntensiveReplyException(string message, Exception innerException)
        : base(KeyLoadTimeSeriesIntensiveProtocol.InvalidReceipt)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(innerException);
    }

    internal KeyLoadTimeSeriesIntensiveReplyException(long? observedSequence)
        : base(KeyLoadTimeSeriesIntensiveProtocol.InvalidReceipt)
    {
        ObservedSequence = observedSequence;
    }

    internal long? ObservedSequence { get; }
}

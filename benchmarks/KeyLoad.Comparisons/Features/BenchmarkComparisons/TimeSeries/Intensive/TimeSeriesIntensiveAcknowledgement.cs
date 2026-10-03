namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensiveAcknowledgement(Guid CommandId, long Sequence)
{
    internal static TimeSeriesIntensiveAcknowledgement FromReceipt(TimeSeriesIntensiveAppendReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Validate(new(receipt.CommandId, receipt.Sequence));
        return new(receipt.CommandId, receipt.Sequence);
    }

    internal static void Validate(TimeSeriesIntensiveAcknowledgement acknowledgement)
    {
        if (acknowledgement.CommandId == Guid.Empty)
        {
            throw new ArgumentException("An acknowledgement requires an actual nonempty command UUID.", nameof(acknowledgement));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(acknowledgement.Sequence, 1L, nameof(acknowledgement));
    }
}

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensiveAcknowledgement(Guid CommandId, long Sequence)
{
    private const long MinimumSequence = 1L;
    private const string MissingCommand = "An acknowledgement requires an actual nonempty command UUID.";
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
            throw new ArgumentException(MissingCommand, nameof(acknowledgement));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(acknowledgement.Sequence, MinimumSequence, nameof(acknowledgement));
    }
}

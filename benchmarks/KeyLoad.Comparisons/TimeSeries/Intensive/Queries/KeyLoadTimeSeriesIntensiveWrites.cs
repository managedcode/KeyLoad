namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class KeyLoadTimeSeriesIntensiveTarget
{
    public async Task<TimeSeriesIntensiveAppendReceipt> AppendAsync(string seriesId, Guid commandId,
        SampleData sample, string tagsJson, CancellationToken cancellationToken)
    {
        EnsureOpen();
        var append = new AppendSamples(Context.SeriesSet, seriesId, [sample], tagsJson);
        var request = new CommandRequest(commandId, Context.Partition, [append]);
        var result = await Context.Peers[KeyLoadTimeSeriesIntensiveProtocol.MeasurementIngressIndex]
            .Client.CommitAsync(request, cancellationToken).ConfigureAwait(false);
        var receipt = KeyLoadTimeSeriesIntensiveResult.Value(result);
        var validated = KeyLoadTimeSeriesIntensiveReceipt.Validate(receipt, commandId, Context.Incarnation,
            Context.ExpectedAtomicPartitionId, Context.SeriesSet, seriesId, expectedRevision: null, expectedCopies: Context.Peers.Length);
        RetainAcknowledgementPosition(validated.Position);
        return validated.Receipt;
    }
}

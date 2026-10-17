using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class KeyLoadTimeSeriesIntensiveTarget
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureOpen();
        var statuses = ImmutableArray.CreateBuilder<NodeStatus>(Context.Peers.Length);
        foreach (var peer in Context.Peers)
        {
            statuses.Add(KeyLoadTimeSeriesIntensiveResult.Value(
                await peer.Client.StatusAsync(cancellationToken).ConfigureAwait(false)));
        }

        var voterIds = Context.Peers.Select(peer => peer.VoterId).ToImmutableArray();
        KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(statuses.MoveToImmutable(), voterIds, Context.Incarnation);
        var requested = new ResourceDefinition(Context.SeriesSet, ResourceKind.TimeSeries,
            Context.Partition.TransactionDomainId);
        var result = await Context.Peers[KeyLoadTimeSeriesIntensiveProtocol.MeasurementIngressIndex]
            .Client.ConfigureResourceAsync(
            TimeSeriesIntensivePlans.CommandId(Context.RunId, KeyLoadTimeSeriesIntensiveProtocol.ConfigurePurpose),
            new(Context.Partition.TenantId, Context.Partition.DatabaseId, requested), cancellationToken)
            .ConfigureAwait(false);
        ValidateResource(KeyLoadTimeSeriesIntensiveResult.Value(result), requested);
    }

    public async Task SeedAsync(string seriesId, ImmutableArray<SampleData> samples, string tagsJson,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        var batch = Volatile.Read(ref seedOrdinal);
        if (batch < KeyLoadTimeSeriesIntensiveProtocol.FirstSeedOrdinal
            || batch >= KeyLoadTimeSeriesIntensiveProtocol.SeedBatchCount
            || seriesId != TimeSeriesIntensiveProfile.SeedSeries || samples.IsDefault
            || samples.Length != KeyLoadTimeSeriesIntensiveProtocol.SeedBatchSize)
        {
            throw new KeyLoadTimeSeriesIntensiveReplyException((long?)null);
        }

        var inFlight = -(batch + KeyLoadTimeSeriesIntensiveProtocol.SeedOrdinalOffset);
        if (Interlocked.CompareExchange(ref seedOrdinal, inFlight, batch) != batch)
        {
            throw new KeyLoadTimeSeriesIntensiveReplyException((long?)null);
        }

        try
        {
            var commandId = SeedCommandId(Context.RunId, batch);
            var mutation = new AppendSamples(Context.SeriesSet, seriesId, samples, tagsJson);
            var request = new CommandRequest(commandId, Context.Partition, [mutation],
                KeyLoadTimeSeriesIntensiveProtocol.OwnershipEpoch);
            var result = await Context.Peers[KeyLoadTimeSeriesIntensiveProtocol.MeasurementIngressIndex]
                .Client.CommitAsync(request, cancellationToken).ConfigureAwait(false);
            var receipt = KeyLoadTimeSeriesIntensiveResult.Value(result);
            var terminalRevision = (batch + KeyLoadTimeSeriesIntensiveProtocol.SeedOrdinalOffset)
                * (long)KeyLoadTimeSeriesIntensiveProtocol.SeedBatchSize;
            var validated = KeyLoadTimeSeriesIntensiveReceipt.Validate(receipt, commandId, Context.Incarnation,
                Context.ExpectedAtomicPartitionId, Context.SeriesSet, seriesId, terminalRevision);
            RetainAcknowledgementPosition(validated.Position);
            if (Interlocked.CompareExchange(ref seedOrdinal,
                batch + KeyLoadTimeSeriesIntensiveProtocol.SeedOrdinalOffset, inFlight) != inFlight)
            {
                throw new KeyLoadTimeSeriesIntensiveReplyException((long?)null);
            }
        }
        catch (Exception)
        {
            Interlocked.CompareExchange(ref seedOrdinal, batch, inFlight);
            throw;
        }
    }

    internal static Guid SeedCommandId(string runId, int batch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentOutOfRangeException.ThrowIfNegative(batch);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(batch,
            KeyLoadTimeSeriesIntensiveProtocol.SeedBatchCount);
        var purpose = KeyLoadTimeSeriesIntensiveProtocol.SeedPurposePrefix + batch.ToString(CultureInfo.InvariantCulture);
        return TimeSeriesIntensivePlans.CommandId(runId, purpose);
    }

    private static void ValidateResource(ResourceDefinition actual, ResourceDefinition expected)
    {
        if (actual.Name != expected.Name || actual.Kind != expected.Kind
            || actual.TransactionDomainId != expected.TransactionDomainId
            || !actual.FieldPolicies.IsDefaultOrEmpty || !actual.HeaderPolicies.IsDefaultOrEmpty)
        {
            throw new KeyLoadTimeSeriesIntensiveReplyException((long?)null);
        }
    }
}

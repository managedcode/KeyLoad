using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Writes
{
    private const long OpenRevision = 1;
    private const long RevisionIncrement = 1;
    private const long NoPosition = 0;

    internal static async Task<ChunkMinimumRf3Window> WindowAsync(RequestCqrsRf3Callers administrator,
        PartitionRef partition, AtomicPartitionPlacementResolution owner, string series, SampleRecord[] rows,
        string node, List<ChunkMinimumRf3Original> originals, CancellationToken token)
    {
        var id = Guid.NewGuid();
        await CommitAsync(administrator, partition, owner, series,
            new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set, series, id,
                TimeSeriesRf3Scenario.Start, DateTimeOffset.MaxValue), SampleChunkProtocol.OpenKind,
            OpenRevision, node, originals, token);
        await CommitAsync(administrator, partition, owner, series,
            new AppendSamples(TimeSeriesRf3Scenario.Set, series, [.. rows.Select(row => row.Sample)],
                TimeSeriesRf3Scenario.PrivateTags), "appendSamples", rows.LongLength, node, originals, token);
        var beforeSeal = checked(OpenRevision + rows.LongLength);
        var revision = checked(beforeSeal + RevisionIncrement);
        var seal = await CommitAsync(administrator, partition, owner, series,
            new SealSampleChunkWindow(TimeSeriesRf3Scenario.Set, series, id, beforeSeal), SampleChunkProtocol.SealKind,
            revision, node, originals, token);
        return new(id, series, revision, rows.LongLength, [.. rows], seal.Token);
    }

    private static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers administrator,
        PartitionRef partition, AtomicPartitionPlacementResolution owner, string series, Mutation mutation, string kind,
        long revision, string node, List<ChunkMinimumRf3Original> originals, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), partition, [mutation])
        {
            OwnershipEpoch = owner.PlacementEpoch
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(request, token));
        await Assert.That(actual.Token.Position).IsGreaterThan(NoPosition);
        var expected = new CommitReceipt(request.CommandId, new(owner.Incarnation, partition.AtomicPartitionId,
            actual.Token.Position, owner.PlacementEpoch),
            [new MutationReceipt(kind, TimeSeriesRf3Scenario.Set, series, revision)], DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        originals.Add(new(request, expected, node));
        return expected;
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityPages
{
    private const string FullScan = "bounded-full-scan";

    internal static async Task HealthyAsync(DistributedQueryAuthorityContext context, DistributedQueryAuthorityState state,
        CancellationToken token)
    {
        var request = RemotePartitionQueryRf3Seed.Request();
        var sql = SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, RemotePartitionQueryRf3Seed.Tool, request);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityCurrent.ObserveAsync(context, state,
            async () => await PageAsync(await McpCallerAssertions.SdkSuccessAsync(
                await context.Reader.PartitionQueryAsync(request, token)), state), token), failures);
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityCurrent.ObserveAsync(context, state,
            async () => await PageAsync((await McpCallerAssertions.SuccessAsync<PartitionQueryPageV1>(
                await context.Official.CallAsync(RemotePartitionQueryRf3Seed.Tool, request, token))).Value, state), token), failures);
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityCurrent.ObserveAsync(context, state,
            async () => await PageAsync(await SqlRf3Protocol.SdkAsync<PartitionQueryPageV1>(context.Reader, sql, token), state), token), failures);
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityCurrent.ObserveAsync(context, state,
            async () => await PageAsync(await SqlRf3Protocol.McpAsync<PartitionQueryPageV1>(context.Official, sql, token), state), token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task PageAsync(PartitionQueryPageV1 actual, DistributedQueryAuthorityState state)
    {
        await Assert.That(actual.Leaves.Length).IsEqualTo(DistributedQueryAuthorityProtocol.LeafCount);
        foreach (var leaf in actual.Leaves)
        {
            await Assert.That(leaf.CutPosition).IsGreaterThan(DistributedQueryAuthorityProtocol.EmptyPosition);
        }
        var rows = new PartitionQueryRowV1[]
        {
            new(RemoteDocumentRf3Protocol.Reference, new(RemoteDocumentRf3Protocol.Document, DistributedQueryAuthorityProtocol.FirstRevision,
                RemoteDocumentRf3Protocol.ProjectedJson, true, [RemoteDocumentRf3Protocol.Secret])),
            new(new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection, RemoteDocumentRf3Protocol.Document),
                new(RemoteDocumentRf3Protocol.Document, DistributedQueryAuthorityProtocol.FirstRevision, RemotePartitionQueryRf3Seed.SourceProjected, true, [RemoteDocumentRf3Protocol.Secret]))
        };
        var expected = new PartitionQueryPageV1(RemotePartitionQueryRf3Seed.Version, [.. rows],
            [new(RemoteDocumentRf3Protocol.Partition, actual.Leaves[DistributedQueryAuthorityProtocol.DestinationLeaf].CutPosition, state.DestinationEpoch, DistributedQueryAuthorityProtocol.InitialSchema, FullScan),
            new(RemotePartitionQueryRf3Seed.Local, actual.Leaves[DistributedQueryAuthorityProtocol.SourceLeaf].CutPosition, DistributedQueryAuthorityProtocol.InitialEpoch, state.SourceSchema, FullScan)], true);
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
}

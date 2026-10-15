using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class HeavyRouteRf3Seed
{
    internal static async Task<HeavyRouteRf3State> RunAsync(RequestCqrsRf3Callers root, CancellationToken token)
    {
        var ann = new NativeAnnMaintenanceRf3Scenario();
        var definition = new ResourceDefinition(HeavyRouteRf3State.Collection, ResourceKind.Collection, ann.Partition.TransactionDomainId)
        {
            VectorProfiles = [new(NativeAnnMaintenanceRf3Scenario.Field, NativeAnnMaintenanceRf3Scenario.Space)]
        };
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
            Guid.NewGuid(), new(ann.Partition.TenantId, ann.Partition.DatabaseId, definition), token)));
        var graph = new ResourceDefinition(HeavyRouteRf3State.Graph, ResourceKind.Graph, ann.Partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(graph, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
            Guid.NewGuid(), new(ann.Partition.TenantId, ann.Partition.DatabaseId, graph), token)));
        var vectors = new float[][] { [1, 0], [0, 1], [-1, 0] };
        var mutations = new List<Mutation>();
        for (var index = 0; index < NativeAnnMaintenanceRf3Scenario.Ids.Length; index++)
        {
            var id = NativeAnnMaintenanceRf3Scenario.Ids[index];
            mutations.Add(new PutDocument(HeavyRouteRf3State.Collection, id, "{}"));
            mutations.Add(new PutVector(HeavyRouteRf3State.Collection, id, NativeAnnMaintenanceRf3Scenario.Field,
                [.. vectors[index]], NativeAnnMaintenanceRf3Scenario.Space, 1L));
        }
        EntityRef Vertex(string id) => new(ann.Partition, HeavyRouteRf3State.Collection, id);
        mutations.Add(new UpsertEdge(HeavyRouteRf3State.Graph, "ab", Vertex("ann-a"), Vertex("ann-b"), "route", "{}"));
        mutations.Add(new UpsertEdge(HeavyRouteRf3State.Graph, "bc", Vertex("ann-b"), Vertex("ann-c"), "route", "{}"));
        var seed = new CommandRequest(Guid.NewGuid(), ann.Partition, [.. mutations]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, ann.Partition), token));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        await ReceiptAsync(seed, placement, receipt);
        var pin = await ann.RequestAsync(root.Sdk, root.Mcp, token);
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.MaintainAnnIndexAsync(pin, token)), pin);
        var idReader = "admission52-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var key = idReader + ".persisted-credential-32-characters";
        var principal = new PrincipalRecord(idReader, ann.Partition.TenantId, [], []);
        var state = new HeavyRouteRf3State(ann, pin, seed, receipt, principal, key,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(ann.Partition, token)));
        await PersistAsync(root, state, 1L, true, token);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), DatabaseEngine.Credential(idReader, idReader, key), token));
        return state;
    }
    private static async Task ReceiptAsync(CommandRequest seed, AtomicPartitionPlacementResolution placement, CommitReceipt receipt)
    {
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(seed.CommandId,
            new(placement.Incarnation, seed.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [new("putDocument", HeavyRouteRf3State.Collection, "ann-a", 1L), new("putVector", HeavyRouteRf3State.Collection, "ann-a", 1L),
             new("putDocument", HeavyRouteRf3State.Collection, "ann-b", 1L), new("putVector", HeavyRouteRf3State.Collection, "ann-b", 1L),
             new("putDocument", HeavyRouteRf3State.Collection, "ann-c", 1L), new("putVector", HeavyRouteRf3State.Collection, "ann-c", 1L),
             new("upsertEdge", HeavyRouteRf3State.Graph, "ab", 1L), new("upsertEdge", HeavyRouteRf3State.Graph, "bc", 1L)],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, receipt);
    }

    internal static async Task PersistAsync(RequestCqrsRf3Callers root, HeavyRouteRf3State state, long epoch, bool allow, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(state.Policy(epoch, allow), await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Policy(epoch, allow), token)));
}

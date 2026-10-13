using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRawReadR43Commands
{
    private const string PutKind = "putDocument";
    private const string EdgeKind = "upsertEdge";

    internal static async Task SeedAsync(RequestCqrsRf3Callers admin, GraphPathRawReadR43State state,
        CancellationToken token)
    {
        state.Placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(
            new(1, state.Seed.Partition), token));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                new AtomicPartitionPlacementReadRequest(1, state.Seed.Partition), token))).Value);
        Mutation[] mutations = [.. state.Vertices.Select(vertex => (Mutation)new PutDocument(vertex.Collection,
            vertex.Id, "{}", Access: new(state.Identity.Principal.Id))),
            .. state.Edges.Select(edge => (Mutation)new UpsertEdge(GraphPathRf3Scenario.Graph, edge.Id,
                edge.From, edge.To, edge.Label, edge.AttributesJson, 0L))];
        state.Command = new(Guid.NewGuid(), state.Seed.Partition, [.. mutations]);
        state.Receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Command, token));
        MutationReceipt[] expected = [.. state.Vertices.Select(vertex => new MutationReceipt(PutKind,
            vertex.Collection, vertex.Id, 1L)), .. state.Edges.Select(edge => new MutationReceipt(EdgeKind,
            GraphPathRf3Scenario.Graph, edge.Id, 1L))];
        await Assert.That(state.Receipt.CommandId).IsEqualTo(state.Command.CommandId);
        await Assert.That(state.Receipt.Token.AtomicPartitionId).IsEqualTo(state.Seed.Partition.AtomicPartitionId);
        await Assert.That(state.Receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(state.Receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(state.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(state.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, state.Receipt.Mutations.ToArray());
        state.Outbox = await GraphPathRawReadR43Current.OutboxAsync(admin, state, token);
    }

    internal static async Task PolicyAsync(RequestCqrsRf3Callers admin, GraphPathRawReadR43State state,
        bool allowed, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            FieldGrants = allowed
                ? [GraphPathRf3Scenario.LabelUseGrant, GraphPathRf3Scenario.LabelReadGrant, GraphPathRf3Scenario.SecretReadGrant]
                : [GraphPathRf3Scenario.LabelUseGrant, GraphPathRf3Scenario.LabelReadGrant],
            PolicyEpoch = checked(state.Identity.Principal.PolicyEpoch + 1)
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, persisted);
        state.Identity = state.Identity with { Principal = persisted };
        state.Allowed = allowed;
        state.Outbox = await GraphPathRawReadR43Current.OutboxAsync(admin, state, token);
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphCrossAuthorityR41Commands
{
    private const string PutKind = "putDocument";
    private const string EdgeKind = "upsertEdge";

    internal static async Task SeedAsync(RequestCqrsRf3Callers administrator,
        GraphCrossAuthorityR41State state, CancellationToken token)
    {
        state.Placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk
            .ReadAtomicPartitionPlacementAsync(new(1, state.Seed.TargetPartition), token));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                new AtomicPartitionPlacementReadRequest(1, state.Seed.TargetPartition), token))).Value);
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.TargetPartition,
            [new PutDocument(state.Collision.Collection, state.Collision.Id, "{}"),
             Upsert(state, state.Foreign, 0L), Upsert(state, state.Local, 0L), Upsert(state, state.Colliding, 0L)]);
        await CommitAsync(administrator, state, command,
            [new(PutKind, state.Collision.Collection, state.Collision.Id, 1L),
             new(EdgeKind, state.Seed.Graph, GraphCrossAuthorityR41State.ForeignId, 1L),
             new(EdgeKind, state.Seed.Graph, GraphCrossAuthorityR41State.LocalId, 1L),
             new(EdgeKind, state.Seed.Graph, GraphCrossAuthorityR41State.CollisionId, 1L)], token);
        state.Outbox = await GraphCrossAuthorityR41Assertions.OutboxAsync(administrator, state, token);
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers administrator,
        GraphCrossAuthorityR41State state, CancellationToken token)
    {
        var next = state.Local with
        {
            AttributesJson = GraphCrossAuthorityR41State.HealthyJson,
            Revision = 2L
        };
        await CommitAsync(administrator, state, new(Guid.NewGuid(), state.Seed.TargetPartition, [Upsert(state, next, 1L)]),
            [new(EdgeKind, state.Seed.Graph, GraphCrossAuthorityR41State.LocalId, 2L)], token);
        state.LocalRevision = 2L;
        state.Outbox = await GraphCrossAuthorityR41Assertions.OutboxAsync(administrator, state, token);
    }

    private static UpsertEdge Upsert(GraphCrossAuthorityR41State state, EdgeRecord edge, long revision)
        => new(state.Seed.Graph, edge.Id, edge.From, edge.To, edge.Label, edge.AttributesJson, revision);

    private static async Task CommitAsync(RequestCqrsRf3Callers administrator, GraphCrossAuthorityR41State state,
        CommandRequest command, MutationReceipt[] mutations, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(mutations, receipt.Mutations.ToArray());
        state.Originals.Add((command, receipt));
    }
}

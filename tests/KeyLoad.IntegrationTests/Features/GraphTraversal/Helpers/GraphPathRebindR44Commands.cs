using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRebindR44Commands
{
    private const string PutKind = "putDocument";
    private const string EdgeKind = "upsertEdge";

    internal static async Task SeedAsync(RequestCqrsRf3Callers admin, GraphPathRebindR44State state,
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
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.Partition, [.. mutations]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        MutationReceipt[] expected = [.. state.Vertices.Select(vertex => new MutationReceipt(PutKind,
            vertex.Collection, vertex.Id, 1L)), .. state.Edges.Select(edge => new MutationReceipt(EdgeKind,
            GraphPathRf3Scenario.Graph, edge.Id, 1L))];
        await ReceiptAsync(receipt, command, expected, state);
        state.Originals.Add((command, receipt));
        state.Outbox = await GraphPathRebindR44Current.OutboxAsync(admin, state, token);
        await GraphPathRebindR44Current.ObserveAsync(admin, state, token);
    }

    internal static async Task ReplaceAsync(RequestCqrsRf3Callers admin, GraphPathRebindR44State state,
        int next, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.Partition,
            [new UpsertEdge(GraphPathRf3Scenario.Graph, "0-sa", state.B, next == 2 ? state.A : state.T,
                GraphPathRf3Scenario.Label, "{}", next - 1L)]);
        var receipt = next == 2 ? await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token))
            : (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, token))).Value;
        await ReceiptAsync(receipt, command, [new(EdgeKind, GraphPathRf3Scenario.Graph, "0-sa", next)], state);
        state.Stage = next;
        state.Originals.Add((command, receipt));
        state.Outbox = await GraphPathRebindR44Current.OutboxAsync(admin, state, token);
        await GraphPathRebindR44Current.ObserveAsync(admin, state, token);
    }

    private static async Task ReceiptAsync(CommitReceipt receipt, CommandRequest command,
        MutationReceipt[] mutations, GraphPathRebindR44State state)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(mutations, receipt.Mutations.ToArray());
    }
}

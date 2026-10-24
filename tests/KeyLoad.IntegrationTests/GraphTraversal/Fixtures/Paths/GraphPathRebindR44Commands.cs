using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphPathRebindR44Commands
{
    private const string PutKind = "putDocument";
    private const string EdgeKind = "upsertEdge";

    internal static async Task SeedAsync(RequestCqrsClusterCallers admin, GraphPathRebindR44State state,
        CancellationToken token)
    {
        Mutation[] mutations = [.. state.Vertices.Select(vertex => (Mutation)new PutDocument(vertex.Collection,
            vertex.Id, "{}", Access: new(state.Identity.Principal.Id))),
            .. state.Edges.Select(edge => (Mutation)new UpsertEdge(GraphPathClusterScenario.Graph, edge.Id,
                edge.From, edge.To, edge.Label, edge.AttributesJson, 0L))];
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.Partition, [.. mutations]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        MutationReceipt[] expected = [.. state.Vertices.Select(vertex => new MutationReceipt(PutKind,
            vertex.Collection, vertex.Id, 1L)), .. state.Edges.Select(edge => new MutationReceipt(EdgeKind,
            GraphPathClusterScenario.Graph, edge.Id, 1L))];
        await ReceiptAsync(receipt, command, expected, state);
        state.Originals.Add((command, receipt));
        state.Outbox = await GraphPathRebindR44Current.OutboxAsync(admin, state, token);
        await GraphPathRebindR44Current.ObserveAsync(admin, state, token);
    }

    internal static async Task ReplaceAsync(RequestCqrsClusterCallers admin, GraphPathRebindR44State state,
        int next, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.Partition,
            [new UpsertEdge(GraphPathClusterScenario.Graph, "0-sa", state.B, next == 2 ? state.A : state.T,
                GraphPathClusterScenario.Label, "{}", next - 1L)]);
        var receipt = next == 2 ? await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token))
            : (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, token))).Value;
        await ReceiptAsync(receipt, command, [new(EdgeKind, GraphPathClusterScenario.Graph, "0-sa", next)], state);
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
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(mutations, receipt.Mutations.ToArray());
    }
}

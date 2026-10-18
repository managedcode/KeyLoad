using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelFrontierPublicCommands
{
    private const string PutKind = "putDocument";
    private const string EdgeKind = "upsertEdge";

    internal static async Task SeedAsync(RequestCqrsClusterCallers admin, GraphLabelFrontierPublicState state,
        CancellationToken token)
    {
        Mutation[] mutations = [.. state.Vertices.Select(vertex => (Mutation)new PutDocument(vertex.Collection,
            vertex.Id, "{}")),
            .. state.Edges.Select(edge => (Mutation)new UpsertEdge(state.Seed.Graph, edge.Id, edge.From, edge.To,
                edge.Label, edge.AttributesJson, 0L))];
        MutationReceipt[] expected = [.. state.Vertices.Select(vertex => new MutationReceipt(PutKind, vertex.Collection, vertex.Id, 1L)),
            .. state.Edges.Select(edge => new MutationReceipt(EdgeKind, state.Seed.Graph, edge.Id, 1L))];
        await CommitAsync(admin, state, new(Guid.NewGuid(), state.Seed.TargetPartition, [.. mutations]), expected, token);
        state.Outbox = await GraphLabelFrontierPublicCurrent.OutboxAsync(admin, state, token);
    }

    internal static async Task ReplaceAsync(RequestCqrsClusterCallers admin, GraphLabelFrontierPublicState state,
        CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.TargetPartition,
            [new UpsertEdge(state.Seed.Graph, "r43-0-sa", state.Start, state.A, "keep", "{}", 1L)]);
        await CommitAsync(admin, state, command, [new(EdgeKind, state.Seed.Graph, "r43-0-sa", 2L)], token);
        state.Replaced = true;
        state.Outbox = await GraphLabelFrontierPublicCurrent.OutboxAsync(admin, state, token);
    }

    private static async Task CommitAsync(RequestCqrsClusterCallers admin, GraphLabelFrontierPublicState state,
        CommandRequest command, MutationReceipt[] mutations, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(mutations, receipt.Mutations.ToArray());
        state.Originals.Add((command, receipt));
    }
}

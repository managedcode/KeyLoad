using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphCycleFrontierPublicCommands
{
    private const string PutKind = "putDocument";
    private const string EdgeKind = "upsertEdge";

    internal static async Task SeedAsync(RequestCqrsClusterCallers admin, GraphCycleFrontierPublicState state,
        CancellationToken token)
    {
        var desired = state.Seed.Reader.Principal with
        {
            RestrictRows = true,
            OwnerId = GraphCycleFrontierPublicState.Owner,
            PolicyEpoch = 2L
        };
        await SqlClusterProtocol.EqualAsync(desired, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), desired, token)));
        Mutation[] mutations = [.. state.Vertices.Select(vertex => (Mutation)new PutDocument(vertex.Collection,
            vertex.Id, "{}", Access: new(GraphCycleFrontierPublicState.Owner))),
            .. state.Edges.Select(edge => (Mutation)new UpsertEdge(state.Seed.Graph, edge.Id, edge.From, edge.To,
                edge.Label, edge.AttributesJson, 0L))];
        MutationReceipt[] expected = [.. state.Vertices.Select(vertex => new MutationReceipt(PutKind, vertex.Collection, vertex.Id, 1L)),
            .. state.Edges.Select(edge => new MutationReceipt(EdgeKind, state.Seed.Graph, edge.Id, 1L))];
        await CommitAsync(admin, state, new(Guid.NewGuid(), state.Seed.TargetPartition, [.. mutations]), expected, token);
        state.Outbox = await GraphCycleFrontierPublicCurrent.OutboxAsync(admin, state, token);
    }

    internal static async Task VisibilityAsync(RequestCqrsClusterCallers admin, GraphCycleFrontierPublicState state,
        bool hidden, CancellationToken token)
    {
        var next = state.BranchRevision + 1L;
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.TargetPartition,
            [new PutDocument(state.A.Collection, state.A.Id, "{}", state.BranchRevision, ExplicitReplacement: true,
                Access: new(hidden ? GraphCycleFrontierPublicState.OtherOwner : GraphCycleFrontierPublicState.Owner))]);
        await CommitAsync(admin, state, command, [new(PutKind, state.A.Collection, state.A.Id, next)], token);
        state.Hidden = hidden;
        state.BranchRevision = next;
        state.Outbox = await GraphCycleFrontierPublicCurrent.OutboxAsync(admin, state, token);
    }

    private static async Task CommitAsync(RequestCqrsClusterCallers admin, GraphCycleFrontierPublicState state,
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

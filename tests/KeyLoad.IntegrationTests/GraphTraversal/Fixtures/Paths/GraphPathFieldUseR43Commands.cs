using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathFieldUseR43Commands
{
    private const string PutKind = "putDocument";
    private const string EdgeKind = "upsertEdge";

    internal static async Task SeedAsync(RequestCqrsClusterCallers admin, GraphPathFieldUseR43State state,
        CancellationToken token)
    {
        Mutation[] mutations = [.. state.Vertices.Select(vertex => (Mutation)new PutDocument(vertex.Collection,
            vertex.Id, "{}", Access: new(state.Identity.Principal.Id))),
            .. state.Edges.Select(edge => (Mutation)new UpsertEdge(GraphPathClusterScenario.Graph, edge.Id,
                edge.From, edge.To, edge.Label, edge.AttributesJson, 0L))];
        state.Command = new(Guid.NewGuid(), state.Seed.Partition, [.. mutations]);
        state.Receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Command, token));
        MutationReceipt[] expected = [.. state.Vertices.Select(vertex => new MutationReceipt(PutKind,
            vertex.Collection, vertex.Id, 1L)), .. state.Edges.Select(edge => new MutationReceipt(EdgeKind,
            GraphPathClusterScenario.Graph, edge.Id, 1L))];
        await Assert.That(state.Receipt.CommandId).IsEqualTo(state.Command.CommandId);
        await Assert.That(state.Receipt.Token.AtomicPartitionId).IsEqualTo(state.Seed.Partition.AtomicPartitionId);
        await Assert.That(state.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(state.Receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(state.Receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(expected, state.Receipt.Mutations.ToArray());
        state.Outbox = await GraphPathFieldUseR43Current.OutboxAsync(admin, state, token);
    }

    internal static async Task PolicyAsync(RequestCqrsClusterCallers admin, GraphPathFieldUseR43State state,
        bool allowed, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            FieldGrants = allowed ? [GraphPathClusterScenario.LabelUseGrant, GraphPathClusterScenario.LabelReadGrant]
                : [GraphPathClusterScenario.LabelReadGrant],
            PolicyEpoch = checked(state.Identity.Principal.PolicyEpoch + 1)
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, persisted);
        state.Identity = state.Identity with
        {
            Principal = persisted
        };
        state.Allowed = allowed;
        state.Outbox = await GraphPathFieldUseR43Current.OutboxAsync(admin, state, token);
    }
}

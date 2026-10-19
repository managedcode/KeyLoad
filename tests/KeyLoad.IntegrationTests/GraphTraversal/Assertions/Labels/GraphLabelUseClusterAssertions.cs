using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelUseClusterAssertions
{
    internal static async Task ReceiptAsync(ClusterFixture fixture, CommandRequest command, CommitReceipt actual)
    {
        var expected = new CommitReceipt(command.CommandId, actual.Token,
            [new("putDocument", GraphLabelUseClusterProtocol.Collection, "a", 1L),
                new("putDocument", GraphLabelUseClusterProtocol.Collection, "b", 1L),
                new("putDocument", GraphLabelUseClusterProtocol.Collection, "c", 1L),
                new("upsertEdge", GraphLabelUseClusterProtocol.Graph, "ab", 1L),
                new("upsertEdge", GraphLabelUseClusterProtocol.Graph, "bc", 1L)], DurabilityProfile.ProcessDurable, 3);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.PersistedCopies).IsEqualTo(3);
        await Assert.That(actual.Token.Incarnation)
            .IsEqualTo(ClusterFixtureProfile.ReadProfile(fixture.Root).Incarnation);
    }

    internal static async Task<string> ImageAsync(RequestCqrsClusterCallers admin, GraphLabelUseClusterState state,
        CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        var failures = new List<Exception>();
        foreach (var id in new[] { "a", "b", "c" })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var reference = GraphLabelUseClusterProtocol.Vertex(state.Seed.Partition, id);
                var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.GetAsync(reference, state.Receipt.Token, token));
                documents.Add(actual);
                await SqlClusterProtocol.EqualAsync(new DocumentReadResult(new(reference, 1L, "{}", false, [])), actual);
            }, failures);
        }
        global::KeyLoad.GraphTraversal? graph = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            graph = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.TraverseAsync(
                GraphLabelUseClusterProtocol.Request(state.Seed.Partition, null), token));
            await SqlClusterProtocol.EqualAsync(GraphLabelUseClusterProtocol.Expected(state.Seed.Partition, null), graph);
        }, failures);
        OutboxStatus? outbox = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Seed.Partition, token));
            await Assert.That(outbox.Head.Tail).IsEqualTo(5L);
            await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(5L);
            await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
            await Assert.That(outbox.Consumers).IsEmpty();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Documents = documents,
            Graph = graph,
            Outbox = outbox
        }, JsonDefaults.Options);
    }
}

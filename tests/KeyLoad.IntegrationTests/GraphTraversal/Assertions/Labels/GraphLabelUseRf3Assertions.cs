using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelUseRf3Assertions
{
    internal static async Task ReceiptAsync(ClusterFixture fixture, CommandRequest command, CommitReceipt actual)
    {
        var expected = new CommitReceipt(command.CommandId, actual.Token,
            [new("putDocument", GraphLabelUseRf3Protocol.Collection, "a", 1L),
                new("putDocument", GraphLabelUseRf3Protocol.Collection, "b", 1L),
                new("putDocument", GraphLabelUseRf3Protocol.Collection, "c", 1L),
                new("upsertEdge", GraphLabelUseRf3Protocol.Graph, "ab", 1L),
                new("upsertEdge", GraphLabelUseRf3Protocol.Graph, "bc", 1L)], DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Token.OwnershipEpoch).IsEqualTo(1L);
        await Assert.That(actual.Token.Incarnation)
            .IsEqualTo(ClusterFixturePhysicalShardIdentity.ReadProfile(fixture.Root).Incarnation);
    }

    internal static async Task<string> ImageAsync(RequestCqrsRf3Callers admin, GraphLabelUseRf3State state,
        CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        var failures = new List<Exception>();
        foreach (var id in new[] { "a", "b", "c" })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var reference = GraphLabelUseRf3Protocol.Vertex(state.Seed.Partition, id);
                var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.GetAsync(reference, state.Receipt.Token, token));
                documents.Add(actual);
                await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new(reference, 1L, "{}", false, [])), actual);
            }, failures);
        }
        global::KeyLoad.GraphTraversal? graph = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            graph = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.TraverseAsync(
                GraphLabelUseRf3Protocol.Request(state.Seed.Partition, null), token));
            await SqlRf3Protocol.EqualAsync(GraphLabelUseRf3Protocol.Expected(state.Seed.Partition, null), graph);
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

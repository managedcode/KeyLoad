using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class CapabilityOutputClusterAssertions
{
    internal static async Task ReceiptAsync(ClusterFixture fixture, CommandRequest command, CommitReceipt actual)
    {
        var expected = new CommitReceipt(command.CommandId, actual.Token,
            [new("putDocument", CapabilityOutputClusterProtocol.Collection, ((PutDocument)command.Mutations[0]).Id, 1L)],
            DurabilityProfile.ProcessDurable, 3);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.PersistedCopies).IsEqualTo(3);
        await Assert.That(actual.Token.Incarnation)
            .IsEqualTo(fixture.Identity.Incarnation);
    }

    internal static async Task<string> ImageAsync(KeyLoadClient root, CapabilityOutputClusterState state, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        var failures = new List<Exception>();
        foreach (var seed in state.Seeds)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var id = ((PutDocument)seed.Command.Mutations[0]).Id;
                var reference = new EntityRef(seed.Command.Partition, CapabilityOutputClusterProtocol.Collection, id);
                var actual = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(reference, seed.Receipt.Token, token));
                documents.Add(actual);
                await SqlClusterProtocol.EqualAsync(new DocumentReadResult(new(state.Stored(id), 1L,
                    CapabilityOutputClusterProtocol.Json(id), false, [])), actual);
            }, failures);
        }
        OutboxStatus? outbox = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(PostgresClusterState.Partition, token));
            await Assert.That(outbox.Head.Tail).IsEqualTo(2L);
            await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(2L);
            await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
            await Assert.That(outbox.Consumers).IsEmpty();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Documents = documents,
            Outbox = outbox
        }, JsonDefaults.Options);
    }
}

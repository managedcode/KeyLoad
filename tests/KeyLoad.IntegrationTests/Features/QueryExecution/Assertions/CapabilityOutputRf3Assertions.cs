using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class CapabilityOutputRf3Assertions
{
    internal static async Task ReceiptAsync(ClusterFixture fixture, CommandRequest command, CommitReceipt actual)
    {
        var expected = new CommitReceipt(command.CommandId, actual.Token,
            [new("putDocument", CapabilityOutputRf3Protocol.Collection, ((PutDocument)command.Mutations[0]).Id, 1L)],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Token.OwnershipEpoch).IsEqualTo(1L);
        await Assert.That(actual.Token.Incarnation)
            .IsEqualTo(ClusterFixturePhysicalShardIdentity.ReadProfile(fixture.Root).Incarnation);
    }

    internal static async Task<string> ImageAsync(KeyLoadClient root, CapabilityOutputRf3State state, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        var failures = new List<Exception>();
        foreach (var seed in state.Seeds)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var id = ((PutDocument)seed.Command.Mutations[0]).Id;
                var reference = new EntityRef(seed.Command.Partition, CapabilityOutputRf3Protocol.Collection, id);
                var actual = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(reference, seed.Receipt.Token, token));
                documents.Add(actual);
                await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new(reference, 1L,
                    CapabilityOutputRf3Protocol.Json(id), false, [])), actual);
            }, failures);
        }
        OutboxStatus? outbox = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(PostgresRf3State.Partition, token));
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

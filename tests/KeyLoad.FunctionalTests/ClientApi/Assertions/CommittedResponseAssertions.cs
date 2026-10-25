using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.OrleansExecution;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class CommittedResponseAssertions
{
    internal static CommitReceipt Expected(ConnectionScenario scenario, CommandRequest command, long position, long revision)
        => new(command.CommandId, new(scenario.Fixture.Database.Store.Identity.Incarnation,
            command.Partition.AtomicPartitionId, position), [new(CommittedResponseProtocol.MutationKind, ConnectionProtocol.Collection, CommittedResponseProtocol.Document, revision)], DurabilityProfile.ProcessDurable,
            scenario.Fixture.Source.Services.GetRequiredService<IOptions<NodeOptions>>().Value.ReplicaCount);

    internal static async Task ReceiptAsync(CommitReceipt? actual, CommitReceipt expected)
    {
        await Assert.That(actual).IsNotNull();
        await Assert.That(OrleansSerialization.Serialize(actual!).SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task StoredAsync(ConnectionScenario scenario, long revision, string json)
    {
        var expected = new DocumentResult(scenario.Reference(CommittedResponseProtocol.Document), revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(scenario.Stored(CommittedResponseProtocol.Document)).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task UnchangedAsync(ConnectionScenario scenario, string[] bytes, long position)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store).SequenceEqual(bytes)).IsTrue();
        await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
    }
}

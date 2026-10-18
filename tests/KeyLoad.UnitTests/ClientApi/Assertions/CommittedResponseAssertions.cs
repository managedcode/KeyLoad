using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class CommittedResponseAssertions
{
    internal static CommitReceipt Expected(ConnectionNativeScenario scenario, CommandRequest command, long position, long revision)
        => new(command.CommandId, new(scenario.Fixture.Database.Store.Identity.Incarnation,
            command.Partition.AtomicPartitionId, position), [new(CommittedResponseProtocol.MutationKind, ConnectionNativeProtocol.Collection, CommittedResponseProtocol.Document, revision)], DurabilityProfile.ProcessDurable, 1);

    internal static async Task ReceiptAsync(CommitReceipt? actual, CommitReceipt expected)
    {
        await Assert.That(actual).IsNotNull();
        await Assert.That(NativeSerialization.Serialize(actual!).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task StoredAsync(ConnectionNativeScenario scenario, long revision, string json)
    {
        var expected = new DocumentResult(scenario.Reference(CommittedResponseProtocol.Document), revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(scenario.Stored(CommittedResponseProtocol.Document)).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task UnchangedAsync(ConnectionNativeScenario scenario, string[] bytes, long position)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store).SequenceEqual(bytes)).IsTrue();
        await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
    }
}

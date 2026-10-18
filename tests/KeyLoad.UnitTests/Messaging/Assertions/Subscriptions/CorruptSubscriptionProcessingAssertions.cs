using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class CorruptSubscriptionProcessingAssertions
{
    internal static async Task RefuseAndRestoreAsync(TestDatabase fixture, SubscriptionRef group,
        Command<DatabaseCommand> operation, SubscriptionDeliveryAuthorityFault fault)
    {
        var key = SubscriptionCheckpointAuthorityFlow.WindowKey(group, 1);
        var original = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The genuine processing lease is missing.");
        var row = NativeSerialization.Deserialize<GroupDelivery>(original);
        fixture.Store.Commit((tx, _) =>
        {
            SubscriptionDeliveryAuthorityFaults.Stage(tx, key, row, fault);
            return true;
        });
        var bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(operation));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo("The subscription delivery window is inconsistent.");
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(fixture.Store, bytes, position, identity);
            await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(
                KeySpace.PartitionOutcome(group.Source.Partition, "root", operation.CommandId)))).IsNull();
        }
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(key, original);
            return true;
        });
    }
}

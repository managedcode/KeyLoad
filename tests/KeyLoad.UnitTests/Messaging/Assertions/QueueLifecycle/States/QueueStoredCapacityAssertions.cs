using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueStoredCapacityAssertions
{
    private const string InvalidCounter = "The queue lifecycle accounting is invalid.";

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition,
        QueueStoredCapacityState state, QueueStoredCapacityFault fault, long position, string[] image)
    {
        if (fault == QueueStoredCapacityFault.UsageWithoutBodies)
        {
            var inspection = Assert.ThrowsExactly<KeyLoadException>(() => owner.InspectMessage(QueueEnqueueColdProtocol.Root,
                new(partition, QueueEnqueueColdProtocol.Queue), QueueEnqueueColdProtocol.Ready));
            await Assert.That(inspection.Code).IsEqualTo(ErrorCode.Corruption);
        }
        else
        {
            var inspection = owner.InspectMessage(QueueEnqueueColdProtocol.Root,
                new(partition, QueueEnqueueColdProtocol.Queue), QueueEnqueueColdProtocol.Ready)!;
            await Assert.That(inspection.PayloadJson).IsEqualTo(QueueEnqueueColdProtocol.ReadyJson);
            await Assert.That(inspection.HeadersJson).IsEqualTo(QueueEnqueueColdProtocol.Headers);
            await Assert.That(inspection.Metadata).IsEqualTo(new MessageMetadata(QueueEnqueueColdProtocol.Ready,
                MessageState.Ready, QueueEnqueueColdProtocol.InitialAttempts, QueueEnqueueColdProtocol.InitialStateVersion,
                QueueEnqueueColdProtocol.FirstReadySequence, null, null));
        }
        await ImageAsync(store, position, image);
        foreach (var operation in new[] { state.Enqueue, state.Receive })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(operation));
            var missingBody = fault == QueueStoredCapacityFault.UsageWithoutBodies && operation != state.Enqueue;
            await Assert.That(error.Code).IsEqualTo(missingBody ? ErrorCode.Corruption : ErrorCode.RecoveryRequired);
            if (!missingBody)
            {
                await Assert.That(error.Message).IsEqualTo(InvalidCounter);
            }
            await ImageAsync(store, position, image);
            await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(partition,
                QueueEnqueueColdProtocol.Root, operation.CommandId)))).IsNull();
            await Assert.That(owner.GetDocument(QueueEnqueueColdProtocol.Root,
                new(partition, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.RefusedDocument))).IsNull();
        }
    }

    internal static async Task ImageAsync(ZoneTreeStore store, long position, string[] image)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}

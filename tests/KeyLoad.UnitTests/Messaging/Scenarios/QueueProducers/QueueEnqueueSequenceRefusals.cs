using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueEnqueueSequenceRefusals
{
    internal static async Task InvalidKeysAsync(DatabaseEngine owner, ZoneTreeStore store,
        QueueLaneRef lane, CommandRequest original, DateTimeOffset time)
    {
        var domain = QueueClaimVersionExhaustionAssertions.Domain(store, lane);
        foreach (var key in new string?[] { null, string.Empty, QueueEnqueueAuthorityProtocol.InvalidOrderingKey })
        {
            var message = original.Mutations.OfType<EnqueueMessage>().Single() with
            {
                OrderingKey = key
            };
            var command = original with
            {
                CommandId = Guid.NewGuid(),
                Mutations = [original.Mutations.OfType<PutDocument>().Single(), message]
            };
            var operation = QueueStoredCapacitySetup.Operation(owner, OperationKind.Batch, command, command.CommandId, time);
            var result = owner.Apply(operation);
            await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
            await Assert.That(result.Problem?.Detail).IsEqualTo(string.IsNullOrEmpty(key)
                ? QueueEnqueueAuthorityProtocol.ExpectedMissingKeyDetail
                : QueueEnqueueAuthorityProtocol.ExpectedIdentifierDetail);

            await Assert.That(result.Value).IsNull();
            await QueueClaimVersionExhaustionAssertions.DomainAsync(store, lane, domain);
            await Assert.That(owner.GetDocument(QueueEnqueueColdProtocol.Root, new(lane.Partition, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Document))).IsNull();
            await QueueStoredCapacityReceipts.FailureAsync(owner, store, lane.Partition, operation, result);
        }
    }
}

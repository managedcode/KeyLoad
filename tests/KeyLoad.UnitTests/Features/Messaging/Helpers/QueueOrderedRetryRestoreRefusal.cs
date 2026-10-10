using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryRestoreRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState original)
    {
        var image = QueueOrderedRetryImage.Capture(database.Store, original.Lane);
        var position = database.Store.Position;
        var prior = original.Outcomes.First(item => item.Operation.Kind == OperationKind.Batch && item.Result.Error is null);
        var publicOperation = prior.Operation with { NativePayload = default };
        var current = database.NormalizeOperation(publicOperation);
        await Assert.That(database.Apply(current).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await QueueOrderedRetryImage.SameAsync(database.Store, original.Lane, image);
        if (original.SecondLease is not null)
        {
            var state = new QueueOrderedRetryState(original.Partition, original.Time, original.ParkedHead, original.Principal);
            await Assert.That(QueueOrderedRetryOperations.Complete(database, state, original.SecondLease, DeliveryAction.Ack).Error)
                .IsEqualTo(ErrorCode.TokenInvalidated);
            await QueueOrderedRetryImage.SameAsync(database.Store, original.Lane, image);
        }
    }
}

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueOrderedRetryRestoreRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState original)
    {
        var image = QueueOrderedRetryImage.Capture(database.Store, original.Lane);
        var position = database.Store.Position;
        var prior = original.Outcomes.First(item => item.Operation.Value!.Kind == OperationKind.Batch && Errors.Code(item.Result.Problem) is null);
        var publicOperation = database.CreateCommand(prior.Operation.Value!.Kind, prior.Operation.CommandId,
            prior.Operation.UserId!, new DateTimeOffset(prior.Operation.Timestamp), prior.Operation.Value.Input);
        var current = database.OwnCommand(publicOperation);
        await Assert.That(Errors.Code(database.Apply(current).Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await QueueOrderedRetryImage.SameAsync(database.Store, original.Lane, image);
        if (original.SecondLease is not null)
        {
            var state = new QueueOrderedRetryState(original.Partition, original.Time, original.ParkedHead, original.Principal);
            await Assert.That(Errors.Code(QueueOrderedRetryOperations.Complete(database, state,
                original.SecondLease, DeliveryAction.Ack).Problem))
                .IsEqualTo(ErrorCode.TokenInvalidated);
            await QueueOrderedRetryImage.SameAsync(database.Store, original.Lane, image);
        }
    }
}

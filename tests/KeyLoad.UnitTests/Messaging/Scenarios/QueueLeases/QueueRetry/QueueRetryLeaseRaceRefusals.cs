namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryLeaseRaceRefusals
{
    internal static async Task OldOwnerAsync(DatabaseEngine database, QueueOrderedRetryState state, Delivery original)
    {
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var renew = QueueOrderedRetryOperations.Complete(database, state, original, DeliveryAction.Renew);
        await Assert.That(Errors.Code(renew.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
        var ack = QueueOrderedRetryOperations.Complete(database, state, original, DeliveryAction.Ack);
        await Assert.That(Errors.Code(ack.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
    }
}

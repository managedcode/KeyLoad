using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryAuthorityRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        var key = KeySpace.Partition("queue-order", state.Partition, state.Lane.Queue,
            QueueOrderedRetryProtocol.SharedKey, QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.First);
        var exact = database.Store.Read(view => view.ReadOwnedValue(key))!;
        var original = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        database.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        var damaged = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var position = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => QueueOrderedRetryOperations.Receive(database, state));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, damaged);
        database.Store.Commit((transaction, _) => { transaction.Put(key, exact); return true; });
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, original);
    }
}

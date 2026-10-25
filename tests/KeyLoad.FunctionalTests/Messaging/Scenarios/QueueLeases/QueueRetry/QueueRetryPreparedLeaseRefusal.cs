using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueRetryPreparedLeaseRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, Command<DatabaseCommand> original)
    {
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var failed = database.Apply(original);
        state.Outcomes.Add((original, failed));
        await Assert.That(Errors.Code(failed.Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
        var replay = database.Apply(original);
        await Assert.That(Errors.Code(replay.Problem).Value).IsEqualTo(Errors.Code(failed.Problem).Value);
        await Assert.That(replay.Problem?.Detail).IsEqualTo(failed.Problem?.Detail);
        await Assert.That(OrleansSerialization.Serialize(replay).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(failed))).IsTrue();
        await Assert.That(replay.Value).IsNull();
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
    }
}

using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryPreparedLeaseRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, ReplicatedOperation original)
    {
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var failed = database.Apply(original);
        state.Outcomes.Add((original, failed));
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.Conflict);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
        var replay = database.Apply(original);
        await Assert.That(replay.Error).IsEqualTo(failed.Error);
        await Assert.That(replay.SafeDetail).IsEqualTo(failed.SafeDetail);
        await Assert.That(replay.Json).IsEqualTo(failed.Json);
        await Assert.That(replay.NativeValue).IsNull();
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
    }
}

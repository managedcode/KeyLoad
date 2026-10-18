using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryPreparedLeaseRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, Command<DatabaseCommand> original)
    {
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var failed = database.Apply(original);
        state.Outcomes.Add((original, failed));
        await Assert.That(Errors.Code(failed.Problem)).IsEqualTo(ErrorCode.Conflict);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
        var replay = database.Apply(original);
        await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(Errors.Code(failed.Problem));
        await Assert.That(replay.Problem?.Detail).IsEqualTo(failed.Problem?.Detail);
        await Assert.That(replay.Json).IsEqualTo(failed.Json);
        await Assert.That(replay.Value).IsNull();
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
    }
}

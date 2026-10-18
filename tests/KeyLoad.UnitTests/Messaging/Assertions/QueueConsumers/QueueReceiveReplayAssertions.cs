using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueReceiveReplayAssertions
{
    internal static async Task SameAsync(IAtomicStore store, PartitionRef partition,
        Command<DatabaseCommand> operation, Result<object> original, bool leaseCurrent, Func<Result<object>> replay)
    {
        var key = KeySpace.PartitionOutcome(partition, operation.UserId!, operation.CommandId);
        var row = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original Receive outcome was not committed.");
        var stored = NativeSerialization.Deserialize<StoredOutcome>(row);
        await NativeReplayResultAssertions.Same<ReceiveResult>(stored.Result, original);
        var actual = replay();
        if (original.Get<ReceiveResult>().Deliveries.IsEmpty || leaseCurrent)
        {
            await NativeReplayResultAssertions.Same<ReceiveResult>(actual, original);
        }
        else
        {
            await Assert.That(actual.Error).IsEqualTo(ErrorCode.StaleLease);
            await Assert.That(actual.SafeDetail).IsEqualTo("The delivery lease is stale.");
            await Assert.That(actual.Json).IsNull();
            await Assert.That(actual.Value).IsNull();
        }
        var after = store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(after is not null && after.AsSpan().SequenceEqual(row)).IsTrue();
    }
}

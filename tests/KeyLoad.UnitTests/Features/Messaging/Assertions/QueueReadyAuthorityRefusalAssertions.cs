using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueReadyAuthorityRefusalAssertions
{
    private const int RetainedReplayFailureIndex = 1;

    internal static ErrorCode ExpectedError(QueueReadyAuthorityFault fault) => fault switch
    {
        QueueReadyAuthorityFault.BodyIdentity or QueueReadyAuthorityFault.MetadataIdentity => ErrorCode.Corruption,
        QueueReadyAuthorityFault.ScheduledState or QueueReadyAuthorityFault.ReadySequence => ErrorCode.RecoveryRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(fault))
    };

    internal static async Task RequireAsync(TestDatabase fixture, ReplicatedOperation original,
        string[] image, long position, byte[] identity)
    {
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(NativeSerialization.Serialize(fixture.Store.Identity)).IsEquivalentTo(identity, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var result = fixture.Database.ResolveOutcome(original);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        var outcome = fixture.Store.Read(view => view.GetRecord<StoredOutcome>(
            KeySpace.PartitionOutcome(fixture.Partition, "worker", original.Id)));
        await Assert.That(outcome).IsNull();
        await Assert.That(fixture.Database.GetDocument("root", new(fixture.Partition, "orders", "input"))).IsNull();
        var inbox = fixture.Store.Read(view => view.Scan(KeySpace.Partition("inbox", fixture.Partition, "jobs"), 1));
        await Assert.That(inbox.Records.IsEmpty).IsTrue();
    }

    internal static async Task RequireOriginalFailuresAsync(List<Exception> failures, ErrorCode expectedError)
    {
        await Assert.That(failures.Count >= 3).IsTrue();
        for (var index = 0; index < failures.Count; index++)
        {
            var failure = failures[index];
            var actualExpectedError = index == RetainedReplayFailureIndex ? ErrorCode.RecoveryRequired : expectedError;
            var errors = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [failure];
            foreach (var error in errors)
            {
                await Assert.That(error is KeyLoadException typed && typed.Code == actualExpectedError).IsTrue();
            }
        }
    }
}

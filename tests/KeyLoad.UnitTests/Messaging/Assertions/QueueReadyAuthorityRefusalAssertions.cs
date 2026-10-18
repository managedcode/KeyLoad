using KeyLoad.Core;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

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

    internal static async Task RequireAsync(TestDatabase fixture, Command<DatabaseCommand> original,
        string[] image, long position, byte[] identity)
    {
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(NativeSerialization.Serialize(fixture.Store.Identity)).IsEquivalentTo(identity, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var result = fixture.Database.ResolveOutcome(original);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.RecoveryRequired);

        await Assert.That(result.Value).IsNull();
        var outcome = fixture.Store.Read(view => view.GetRecord<StoredOutcome>(
            KeySpace.PartitionOutcome(fixture.Partition, "worker", original.CommandId)));
        await Assert.That(outcome).IsNull();
        await Assert.That(fixture.Database.GetDocument("root", new(fixture.Partition, "orders", "input"))).IsNull();
        var inbox = fixture.Store.Read(view => view.Scan(KeySpace.Partition("inbox", fixture.Partition, "jobs"), 1));
        await Assert.That(inbox.Records.IsEmpty).IsTrue();
    }

    internal static async Task RequireRepairedReplayAsync(TestDatabase fixture, Command<DatabaseCommand> original,
        ReceiveResult first)
    {
        var resultBytes = NativeSerialization.Serialize(first);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        var position = fixture.Store.Position;
        var cut = fixture.ReadCanonicalCut();
        var replay = fixture.SubmitIssued(original).Get<ReceiveResult>();
        await Assert.That(NativeSerialization.Serialize(replay)).IsEquivalentTo(resultBytes, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(NativeSerialization.Serialize(fixture.Store.Identity)).IsEquivalentTo(identity, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
    }

    internal static async Task RequireOriginalFailuresAsync(List<Exception> failures, ErrorCode expectedError)
    {
        await Assert.That(failures.Count >= 3).IsTrue();
        for (var index = 0; index < failures.Count; index++)
        {
            var failure = failures[index];
            var actualExpectedError = index <= RetainedReplayFailureIndex ? ErrorCode.RecoveryRequired : expectedError;
            var errors = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [failure];
            foreach (var error in errors)
            {
                await Assert.That(error is KeyLoadException typed && typed.Code == actualExpectedError).IsTrue();
            }
        }
    }
}

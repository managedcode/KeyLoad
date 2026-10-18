using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;
using A = KeyLoad.UnitTests.Features.DocumentStorage.DocumentSessionAuthorityColdAssertions;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentSessionAuthorityContinuation
{
    internal static async Task RunAsync(DocumentPatchAtomicityOwner fixture,
        Command<DatabaseCommand> original, CommitReceipt first)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var foreign = Assert.ThrowsExactly<KeyLoadException>(() => A.Read(fixture,
            first.Token with { AtomicPartitionId = "foreign-atomic-partition" }));
        await Assert.That(foreign.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() =>
            A.Read(fixture, first.Token, cancellation.Token));
        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await A.UnchangedAsync(fixture, image, position);
        var id = Guid.NewGuid();
        var next = checked(fixture.Database.LastApplied + 1);
        var request = new CommandRequest(id, fixture.Source.Partition,
            [new PutDocument(A.Collection, A.Id, A.Json, 1, ExplicitReplacement: true)]);
        var operation = fixture.Database.CreateCommand(OperationKind.Batch, id, A.Root,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var expected = new CommitReceipt(id, new(fixture.Store.Identity.Incarnation,
            fixture.Source.Partition.AtomicPartitionId, next), [new("putDocument", A.Collection, A.Id, 2)],
            DurabilityProfile.ProcessDurable, 1);
        await DocumentPatchAtomicityAssertions.BytesAsync(NativeSerialization.Serialize(expected),
            NativeSerialization.Serialize(fixture.Submit(operation).Get<CommitReceipt>()));
        await A.ReplayAsync(fixture, operation, expected);
        await fixture.ReopenAsync();
        await A.LiteralAsync(fixture, first.Token, 2);
        await A.LiteralAsync(fixture, expected.Token, 2);
        await A.ReplayAsync(fixture, original, first);
        await A.ReplayAsync(fixture, operation, expected);
    }
}

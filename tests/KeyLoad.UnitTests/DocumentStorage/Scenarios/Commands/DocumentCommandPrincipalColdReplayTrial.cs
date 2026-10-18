using ManagedCode.Communication.Commands;
using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentCommandPrincipalColdReplayContract;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentCommandPrincipalColdReplayTrial
{
    internal static async Task RunAsync(DocumentPatchAtomicityOwner owner)
    {
        owner.Source.Configure(Collection, ResourceKind.Collection);
        DocumentCommandOutcomeTestSupport.ConfigureWriter(owner.Source, FirstPrincipal);
        DocumentCommandOutcomeTestSupport.ConfigureWriter(owner.Source, SecondPrincipal);
        var baseline = DocumentPatchAtomicityState.Read(owner);
        var tail = owner.Database.GetOutboxStatus("root", owner.Source.Partition).Head.Tail;
        var sharedId = Guid.NewGuid();
        var first = Commit(owner, Create(owner, sharedId, FirstPrincipal, FirstDocument, FirstJson, 0));
        var second = Commit(owner, Create(owner, sharedId, SecondPrincipal, SecondDocument, SecondJson, 0));
        await Assert.That(first.Entry.Index).IsEqualTo(baseline.LastIndex + 1L);
        await Assert.That(second.Entry.Index).IsEqualTo(baseline.LastIndex + 2L);
        await DocumentCommandPrincipalColdReplayAssertions.IndependentAsync(owner, first, second, tail + 2L);
        await DocumentCommandPrincipalColdReplayAssertions.DocumentsAsync(owner, healthy: false);
        await ConflictsAsync(owner, sharedId, first, second);
        var warm = DocumentPatchAtomicityState.Read(owner);
        await owner.ReopenAsync();
        await warm.RequireAsync(DocumentPatchAtomicityState.Read(owner));
        await DocumentCommandPrincipalColdReplayAssertions.IndependentAsync(owner, first, second, tail + 2L);
        await ConflictsAsync(owner, sharedId, first, second);
        await DocumentCommandPrincipalColdReplayAssertions.DocumentsAsync(owner, healthy: false);
        var healthy = Commit(owner, Create(owner, Guid.NewGuid(), FirstPrincipal, FirstDocument, ChangedJson, 1));
        await DocumentCommandPrincipalColdReplayAssertions.ReceiptAsync(owner, healthy, FirstDocument, 2);
        await DocumentCommandPrincipalColdReplayAssertions.DocumentsAsync(owner, healthy: true);
        await DocumentCommandPrincipalColdReplayAssertions.IndependentAsync(owner, first, second, tail + 3L);
        await ConflictsAsync(owner, sharedId, first, second);
        var continued = DocumentPatchAtomicityState.Read(owner);
        await owner.ReopenAsync();
        await continued.RequireAsync(DocumentPatchAtomicityState.Read(owner));
        await DocumentCommandPrincipalColdReplayAssertions.DocumentsAsync(owner, healthy: true);
        await DocumentCommandPrincipalColdReplayAssertions.IndependentAsync(owner, first, second, tail + 3L);
        await DocumentCommandPrincipalColdReplayAssertions.ReplayAsync(owner, healthy);
        await ConflictsAsync(owner, sharedId, first, second);
        await continued.RequireAsync(DocumentPatchAtomicityState.Read(owner));
    }

    private static Retained Commit(DocumentPatchAtomicityOwner owner, Command<DatabaseCommand> original)
    {
        var result = owner.Submit(original);
        result.Get<CommitReceipt>();
        return new(owner.Entry(original), result);
    }

    private static async Task ConflictsAsync(DocumentPatchAtomicityOwner owner, Guid sharedId,
        Retained first, Retained second)
    {
        var before = DocumentPatchAtomicityState.Read(owner);
        var changedFirst = Create(owner, sharedId, FirstPrincipal, FirstDocument, ChangedJson, 1);
        var changedSecond = Create(owner, sharedId, SecondPrincipal, SecondDocument, ChangedJson, 1);
        await Assert.That(Errors.Code(owner.Submit(changedFirst).Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(Errors.Code(owner.Submit(changedSecond).Problem)).IsEqualTo(ErrorCode.Conflict);
        await DocumentCommandPrincipalColdReplayAssertions.ReplayAsync(owner, first);
        await DocumentCommandPrincipalColdReplayAssertions.ReplayAsync(owner, second);
        await before.RequireAsync(DocumentPatchAtomicityState.Read(owner));
    }

    private static Command<DatabaseCommand> Create(DocumentPatchAtomicityOwner owner, Guid id,
        string principal, string document, string json, long revision)
    {
        var request = new CommandRequest(id, owner.Source.Partition, [new PutDocument(Collection, document, json, revision)]);
        return owner.Database.CreateCommand(OperationKind.Batch, id, principal,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
    }
}

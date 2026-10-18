using ManagedCode.Communication.Commands;
using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentPatchAtomicityContract;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentPatchAtomicityTrial
{
    internal static async Task RunAsync(DocumentPatchAtomicityOwner owner, int fault)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fault);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fault, 1);
        owner.Source.Configure(Collection, ResourceKind.Collection,
            indexes: [new(DocumentPatchAtomicityContract.Index, [ValuePath], Unique: true)]);
        var seed = Create(owner, new PutDocument(Collection, First, FirstBefore, 0),
            new PutDocument(Collection, Second, fault == 0 ? MissingBefore : ArrayBefore, 0));
        owner.Submit(seed).Get<CommitReceipt>();
        await DocumentPatchAtomicityAssertions.DocumentsAsync(owner, fault, healthy: false);
        await DocumentPatchAtomicityAssertions.IndexAsync(owner, 1, First);
        await DocumentPatchAtomicityAssertions.IndexAsync(owner, 10, Second);
        var originalTail = owner.Database.GetOutboxStatus(Root, owner.Source.Partition).Head.Tail;
        var before = DocumentPatchAtomicityState.Read(owner);
        var operation = Batch(owner, fault, secondRevision: 1);
        var failed = owner.Submit(operation);
        var retained = owner.Entry(operation.Id);
        await DocumentPatchAtomicityAssertions.BytesAsync(NativeSerialization.Serialize(operation),
            NativeSerialization.Serialize(retained.PreparedCommand));
        await DocumentPatchAtomicityAssertions.FailureAsync(failed, fault);
        await before.RequireDomainAsync(DocumentPatchAtomicityState.Read(owner));
        await Assert.That(retained.CanonicalSequence).IsEqualTo(before.LastIndex + 1L);
        await DocumentPatchAtomicityAssertions.ReplayAsync(owner, retained, failed);
        var failedState = DocumentPatchAtomicityState.Read(owner);
        await owner.ReopenAsync();
        await failedState.RequireAsync(DocumentPatchAtomicityState.Read(owner));
        await DocumentPatchAtomicityAssertions.DocumentsAsync(owner, fault, healthy: false);
        await DocumentPatchAtomicityAssertions.ReplayAsync(owner, retained, failed);
        var repair = Create(owner, new PutDocument(Collection, Second,
            fault == 0 ? MissingRepair : ArrayRepair, 1));
        var repaired = owner.Submit(repair).Get<CommitReceipt>();
        await Assert.That(repaired.Mutations.Single().Revision).IsEqualTo(2L);
        await Assert.That(owner.Database.GetOutboxStatus(Root, owner.Source.Partition).Head.Tail)
            .IsEqualTo(originalTail + 1L);
        await DocumentPatchAtomicityAssertions.ReplayAsync(owner, retained, failed);
        var fresh = Batch(owner, fault, secondRevision: 2);
        var healthy = owner.Submit(fresh);
        var healthyEntry = owner.Entry(fresh.Id);
        await DocumentPatchAtomicityAssertions.BytesAsync(NativeSerialization.Serialize(fresh),
            NativeSerialization.Serialize(healthyEntry.PreparedCommand));
        await DocumentPatchAtomicityAssertions.HealthyAsync(owner, fault, healthyEntry, healthy, originalTail + 3L);
        await DocumentPatchAtomicityAssertions.ReplayAsync(owner, retained, failed);
        var healthyState = DocumentPatchAtomicityState.Read(owner);
        await owner.ReopenAsync();
        await healthyState.RequireAsync(DocumentPatchAtomicityState.Read(owner));
        await DocumentPatchAtomicityAssertions.HealthyAsync(owner, fault, healthyEntry, healthy, originalTail + 3L);
        await DocumentPatchAtomicityAssertions.ReplayAsync(owner, retained, failed);
        await healthyState.RequireAsync(DocumentPatchAtomicityState.Read(owner));
    }

    private static Command<DatabaseCommand> Batch(DocumentPatchAtomicityOwner owner, int fault, long secondRevision)
        => Create(owner, new PutDocument(Collection, First, FirstAfter, 1),
            new PatchDocument(Collection, Second,
                [new(ValuePath, PatchKind.Set, "11"), new(fault == 0 ? MissingPath : ArrayPath, PatchKind.Set, "5")],
                secondRevision));

    private static Command<DatabaseCommand> Create(DocumentPatchAtomicityOwner owner, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, owner.Source.Partition, [.. mutations]);
        return owner.Database.CreateCommand(OperationKind.Batch, id, Root,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
    }
}

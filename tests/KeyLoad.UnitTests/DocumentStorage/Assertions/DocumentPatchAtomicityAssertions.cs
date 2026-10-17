using KeyLoad.Query;
using KeyLoad.Replication;
using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentPatchAtomicityContract;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentPatchAtomicityAssertions
{
    internal static async Task BytesAsync(byte[] expected, byte[] actual)
        => await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();

    internal static async Task FailureAsync(OperationResult result, int fault)
    {
        await Assert.That(result.Error).IsEqualTo(fault == 0 ? ErrorCode.Validation : ErrorCode.UnsupportedCapability);
        await Assert.That(result.SafeDetail).IsEqualTo(fault == 0 ? MissingDetail : ArrayDetail);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }

    internal static async Task ReplayAsync(DocumentPatchAtomicityOwner owner,
        ReplicaEntry original, OperationResult result)
    {
        var operation = original.Operation
            ?? throw new InvalidOperationException("The retained document command is missing.");
        var before = DocumentPatchAtomicityState.Read(owner);
        var stored = OutcomeStoreOracle.ReadPartition(owner.Store, owner.Source.Partition, Root, operation.Id)
            ?? throw new InvalidOperationException("The original scoped native outcome is missing.");
        await BytesAsync(NativeSerialization.Serialize(result), NativeSerialization.Serialize(stored));
        await Assert.That(stored.Error).IsEqualTo(result.Error);
        await Assert.That(stored.SafeDetail).IsEqualTo(result.SafeDetail);
        if (result.Error is null)
        {
            await BytesAsync(NativeSerialization.Serialize(result.Get<CommitReceipt>()), NativeSerialization.Serialize(stored.Get<CommitReceipt>()));
        }
        else
        {
            await Assert.That(stored.NativeValue).IsNull();
        }
        var entryBytes = NativeSerialization.Serialize(original);
        var resultBytes = NativeSerialization.Serialize(result);
        await BytesAsync(resultBytes, NativeSerialization.Serialize(owner.Submit(operation)));
        await BytesAsync(resultBytes, NativeSerialization.Serialize(owner.Database.ResolveOutcome(operation)));
        await BytesAsync(entryBytes, NativeSerialization.Serialize(owner.Entry(operation.Id)));
        await before.RequireAsync(DocumentPatchAtomicityState.Read(owner));
    }

    internal static async Task DocumentsAsync(DocumentPatchAtomicityOwner owner, int fault, bool healthy)
    {
        var first = owner.Database.GetDocument(Root, new(owner.Source.Partition, Collection, First))!;
        var second = owner.Database.GetDocument(Root, new(owner.Source.Partition, Collection, Second))!;
        await Assert.That(first.Revision).IsEqualTo(healthy ? 2L : 1L);
        await Assert.That(first.Json).IsEqualTo(healthy ? FirstAfter : FirstBefore);
        await Assert.That(second.Revision).IsEqualTo(healthy ? 3L : 1L);
        await Assert.That(second.Json).IsEqualTo(healthy ? (fault == 0 ? MissingAfter : ArrayAfter)
            : (fault == 0 ? MissingBefore : ArrayBefore));
    }

    internal static async Task IndexAsync(DocumentPatchAtomicityOwner owner, int value, params string[] ids)
    {
        var page = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution())
            .Execute(Root, new(owner.Source.Partition, $"SELECT * FROM \"{Collection}\" WHERE value = {value}"));
        await Assert.That(page.AccessPath).IsEqualTo("index:" + DocumentPatchAtomicityContract.Index);
        await Assert.That(page.Rows.Select(row => row.EntityId))
            .IsEquivalentTo(ids, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task HealthyAsync(DocumentPatchAtomicityOwner owner, int fault,
        ReplicaEntry original, OperationResult result, long outboxTail)
    {
        var operation = original.Operation
            ?? throw new InvalidOperationException("The retained document command is missing.");
        await Assert.That(result.Error).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.Id);
        await Assert.That(receipt.Token.Position).IsEqualTo(original.Index);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(owner.Source.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(owner.Database.Durability);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(2);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(First);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(2L);
        await Assert.That(receipt.Mutations[1].Id).IsEqualTo(Second);
        await Assert.That(receipt.Mutations[1].Revision).IsEqualTo(3L);
        await DocumentsAsync(owner, fault, healthy: true);
        await IndexAsync(owner, 1);
        await IndexAsync(owner, 10);
        await IndexAsync(owner, 2, First);
        await IndexAsync(owner, 11, Second);
        await Assert.That(owner.Database.GetOutboxStatus(Root, owner.Source.Partition).Head.Tail)
            .IsEqualTo(outboxTail);
        await ReplayAsync(owner, original, result);
    }
}

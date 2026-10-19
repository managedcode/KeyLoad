using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentCommandPrincipalColdReplayContract;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentCommandPrincipalColdReplayAssertions
{
    internal static async Task IndependentAsync(DocumentPatchAtomicityOwner owner,
        Retained first, Retained second, long tail)
    {
        await ReceiptAsync(owner, first, FirstDocument, 1);
        await ReceiptAsync(owner, second, SecondDocument, 1);
        await Assert.That(first.Entry.CanonicalSequence == second.Entry.CanonicalSequence).IsFalse();
        await Assert.That(OrleansSerialization.Serialize(first.Result)
            .AsSpan().SequenceEqual(OrleansSerialization.Serialize(second.Result))).IsFalse();
        await ReplayAsync(owner, first);
        await ReplayAsync(owner, second);
        await Assert.That(owner.Database.GetOutboxStatus("root", owner.Source.Partition).Head.Tail).IsEqualTo(tail);
        Exception? ambiguous = null;
        try
        {
            owner.Entry(first.Entry.PreparedCommand.CommandId);
        }
        catch (InvalidOperationException error) { ambiguous = error; }
        await Assert.That(ambiguous).IsNotNull();
        await Assert.That(ambiguous!.Message)
            .IsEqualTo("The exact stored test outcome is missing or ambiguous.");
    }

    internal static async Task ReceiptAsync(DocumentPatchAtomicityOwner owner,
        Retained original, string document, long revision)
    {
        var operation = original.Entry.PreparedCommand;
        await Assert.That(Errors.Code(original.Result.Problem)).IsNull();
        var receipt = original.Result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(original.Entry.CanonicalSequence);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(owner.Source.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(owner.Database.Durability);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo("putDocument");
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(Collection);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(document);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(revision);
    }

    internal static async Task ReplayAsync(DocumentPatchAtomicityOwner owner, Retained original)
    {
        var operation = original.Entry.PreparedCommand;
        var before = DocumentPatchAtomicityState.Read(owner);
        var stored = OutcomeStoreOracle.ReadPartition(owner.Store, owner.Source.Partition, operation.UserId!, operation.CommandId)
            ?? throw new InvalidOperationException("The original scoped outcome is missing.");
        await DocumentPatchAtomicityAssertions.BytesAsync(OrleansSerialization.Serialize(original.Result), OrleansSerialization.Serialize(stored));
        await DocumentPatchAtomicityAssertions.BytesAsync(OrleansSerialization.Serialize(original.Result), OrleansSerialization.Serialize(owner.Submit(operation)));
        var resolved = owner.Database.ResolveOutcome(operation);
        await DocumentPatchAtomicityAssertions.BytesAsync(OrleansSerialization.Serialize(original.Result), OrleansSerialization.Serialize(resolved));
        await DocumentPatchAtomicityAssertions.BytesAsync(OrleansSerialization.Serialize(original.Entry), OrleansSerialization.Serialize(owner.Entry(operation)));
        await before.RequireAsync(DocumentPatchAtomicityState.Read(owner));
    }

    internal static async Task DocumentsAsync(DocumentPatchAtomicityOwner owner, bool healthy)
    {
        var first = owner.Database.GetDocument("root", new(owner.Source.Partition, Collection, FirstDocument));
        var second = owner.Database.GetDocument("root", new(owner.Source.Partition, Collection, SecondDocument));
        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(first!.Json).IsEqualTo(healthy ? ChangedJson : FirstJson);
        await Assert.That(first.Revision).IsEqualTo(healthy ? 2L : 1L);
        await Assert.That(second!.Json).IsEqualTo(SecondJson);
        await Assert.That(second.Revision).IsEqualTo(1L);
    }
}

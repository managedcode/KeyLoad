using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlCallNoPartialWriteAssertions
{
    private const string MutationKind = "putDocument";
    private const long InitialRevision = 1;

    internal static async Task OriginalAsync(DatabaseEngine database, PartitionRef partition, CommitReceipt receipt, Guid id)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await SameAsync(new[] {
            new MutationReceipt(MutationKind, SqlBetweenTestData.Collection, SqlCallNoPartialWriteContinuation.FirstId, InitialRevision),
            new MutationReceipt(MutationKind, SqlBetweenTestData.Collection, SqlCallNoPartialWriteContinuation.SecondId, InitialRevision)
        }, receipt.Mutations);
        await DocumentAsync(database, partition, SqlCallNoPartialWriteContinuation.FirstId, SqlCallNoPartialWriteContinuation.FirstBody);
        await DocumentAsync(database, partition, SqlCallNoPartialWriteContinuation.SecondId, SqlCallNoPartialWriteContinuation.SecondBody);
    }

    internal static async Task HealthyAsync(DatabaseEngine database, PartitionRef partition, CommitReceipt receipt, Guid id)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await SameAsync(new[] { new MutationReceipt(MutationKind, SqlBetweenTestData.Collection,
            SqlCallNoPartialWriteContinuation.HealthyId, InitialRevision) }, receipt.Mutations);
        await DocumentAsync(database, partition, SqlCallNoPartialWriteContinuation.HealthyId, SqlCallNoPartialWriteContinuation.HealthyBody);
    }

    private static async Task DocumentAsync(DatabaseEngine database, PartitionRef partition, string id, string body)
    {
        var reference = new EntityRef(partition, SqlBetweenTestData.Collection, id);
        var expected = new DocumentResult(reference, InitialRevision, body, false, []);
        await SameAsync(expected, database.GetDocument(SqlBetweenTestData.Root, reference));
    }

    internal static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long cut)
    {
        await Assert.That(store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await SameAsync(expected, actual);
        await Assert.That(NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(
            NativeSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task SameAsync<TExpected, TActual>(TExpected expected, TActual actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}

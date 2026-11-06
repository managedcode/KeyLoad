using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class CanonicalKeyContractTests
{
    private const string AppliedFamily = "command-applied";
    private const string Metadata = "metadata";
    private const string Collection = "documents";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "orders";
    private const string FirstKey = "customer-1";
    private const string SecondKey = "customer-2";

    [Test]
    public async Task AcRoc002CommandKeysKeepOwnedBytesAndSeparateMetadataFromCaseSensitivePartitions()
    {
        var partition = new PartitionRef(Tenant, Database, Domain, FirstKey);
        var applied = StorageCommandKeys.Applied(partition);
        var owned = StorageCommandKeys.Applied(partition);
        owned[0] ^= byte.MaxValue;
        await Assert.That(applied).IsEquivalentTo(KeyCodec.Encode(AppliedFamily, Tenant, Database, Domain, FirstKey));
        await Assert.That(StorageCommandKeys.Applied(null)).IsEquivalentTo(KeyCodec.Encode(AppliedFamily, Metadata));
        await Assert.That(applied.AsSpan().SequenceEqual(owned)).IsFalse();
        await Assert.That(applied.AsSpan().SequenceEqual(StorageCommandKeys.Applied(partition with { PartitionKey = FirstKey.ToUpperInvariant() }))).IsFalse();
    }

    [Test]
    public async Task AcRoc002RealCommandsAdvanceOnlyTheirOwnPartitionPrefix()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var second = database.Partition with { PartitionKey = SecondKey };
        var metadata = database.Store.Read(view => StorageOperationApply.ReadCanonicalSequence(view, null));
        var first = database.Commit(new PutDocument(Collection, FirstKey, "{}"));
        var secondRequest = new CommandRequest(Guid.NewGuid(), second, [new PutDocument(Collection, SecondKey, "{}")]);
        var other = database.Submit(OperationKind.Batch, secondRequest, id: secondRequest.CommandId).Get<CommitReceipt>();
        var next = database.Commit(new PutDocument(Collection, FirstKey, "{\"updated\":true}", 1));
        await Assert.That(first.Token.Position).IsEqualTo(1L);
        await Assert.That(other.Token.Position).IsEqualTo(1L);
        await Assert.That(next.Token.Position).IsEqualTo(2L);
        await Assert.That(first.Token.AtomicPartitionId).IsNotEqualTo(other.Token.AtomicPartitionId);
        await Assert.That(database.Store.Read(view => StorageOperationApply.ReadCanonicalSequence(view, database.Partition))).IsEqualTo(2L);
        await Assert.That(database.Store.Read(view => StorageOperationApply.ReadCanonicalSequence(view, second))).IsEqualTo(1L);
        await Assert.That(database.Store.Read(view => StorageOperationApply.ReadCanonicalSequence(view, null))).IsEqualTo(metadata);
    }

    [Test]
    public async Task AcRoc002PartitionCopyComputesItsOwnAtomicIdentity()
    {
        var first = new PartitionRef(Tenant, Database, Domain, FirstKey);
        var original = first.AtomicPartitionId;
        var moved = first with { PartitionKey = SecondKey };
        var unchanged = first with { };

        await Assert.That(moved.AtomicPartitionId)
            .IsEqualTo(new PartitionRef(Tenant, Database, Domain, SecondKey).AtomicPartitionId);
        await Assert.That(moved.AtomicPartitionId).IsNotEqualTo(original);
        await Assert.That(unchanged.AtomicPartitionId).IsEqualTo(original);
        await Assert.That(first.AtomicPartitionId).IsEqualTo(original);
    }
}

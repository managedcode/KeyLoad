using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedMalformedStateTests
{
    [Test]
    public async Task MalformedMatchingVectorFailsAsCorruptionWithoutFurtherWrites()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var malformed = new VectorRecord(AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), ImmutableArray.Create(float.NaN, 1f, 2f), 1);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(VectorKey(database, malformed.DocumentId), malformed);
            return true;
        });
        var position = database.Store.Position;
        var failure = AnnSeedTestSupport.CaptureFailure(database, "root");

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task MalformedPersistedPartitionIdentityFailsAsCorruption()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var document = new DocumentRecord(
            new(new(null!, database.Partition.DatabaseId, database.Partition.TransactionDomainId,
                database.Partition.PartitionKey), AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0)),
            1, "{}", new(AnnSeedTestSupport.Owner), DateTimeOffset.UnixEpoch);
        using var serializer = new NativeSerializerFixture();
        var corrupted = serializer.Encode(document);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(DocumentStorageKeys.RecordKey(database.Partition, AnnSeedTestSupport.Collection,
                AnnSeedTestSupport.Id(0)), corrupted);
            return true;
        });
        var position = database.Store.Position;

        var failure = AnnSeedTestSupport.CaptureFailure(database, "root");

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task StructurallyValidPersistedPartitionMismatchFailsAsCorruption()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var document = new DocumentRecord(
            new(database.Partition with { TenantId = "different-persisted-tenant" },
                AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0)),
            1, "{}", new(AnnSeedTestSupport.Owner), DateTimeOffset.UnixEpoch);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(DocumentStorageKeys.RecordKey(database.Partition, AnnSeedTestSupport.Collection,
                AnnSeedTestSupport.Id(0)), document);
            return true;
        });
        var position = database.Store.Position;

        var failure = AnnSeedTestSupport.CaptureFailure(database, "root");

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task DuplicateRetainedDocumentIdentityFailsAsCorruption()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var duplicate = new VectorRecord(AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), ImmutableArray.Create(0.25f, 1f, -0.5f), 1);
        var aliasKey = KeySpace.Partition("vector", database.Partition, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Field, "alternate-storage-key");
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(aliasKey, duplicate);
            return true;
        });
        var position = database.Store.Position;

        var failure = AnnSeedTestSupport.CaptureFailure(database, "root");

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task InvalidAppliedScalarCutFailsAsCorruption()
    {
        using var database = AnnSeedTestSupport.Create(1);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(KeySpace.AppliedBytes, NativeSerialization.Serialize(-1L));
            return true;
        });
        var position = database.Store.Position;

        var failure = AnnSeedTestSupport.CaptureFailure(database, "root");

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task WorkLimitExhaustionDuringMatchingRecordValidationRemainsBudgetExceeded()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var accepted = AnnSeedTestSupport.Capture(database);
        var malformedSpace = AnnSeedTestSupport.Space(model: new string('\0', 256));
        var malformed = new VectorRecord(AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field,
            malformedSpace, ImmutableArray.Create(0.25f, 1f, -0.5f), 1);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(VectorKey(database, malformed.DocumentId), malformed);
            return true;
        });

        var failure = AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal,
            options: new() { MaxWorkUnits = accepted.WorkUnits });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);

        AnnSeedTestSupport.CommitVector(database, AnnSeedTestSupport.Id(0),
            ImmutableArray.Create(0.25f, 1f, -0.5f));
        var healthy = AnnSeedTestSupport.Capture(database);
        await Assert.That(healthy.Records).HasSingleItem();
    }

    private static byte[] VectorKey(TestDatabase database, string id)
        => KeySpace.Partition("vector", database.Partition, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Field, id);
}

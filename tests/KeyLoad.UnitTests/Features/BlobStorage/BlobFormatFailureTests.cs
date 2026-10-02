using System.Text;
using System.Text.Json.Nodes;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobFormatFailureTests
{
    private const string PrincipalId = "root";
    private const string BlobId = "unknown-format";
    private const string StateSpace = "blob-state-v1";
    private const string PartSpace = "blob-part-v1";
    private const string PartMetadataSpace = "blob-partmeta-v1";
    private const string FormatVersionProperty = "formatVersion";
    private const string GuidFormat = "N";
    private const int UnknownFormatVersion = 99;
    private const int PartOrdinal = 0;
    private const int PartCount = 1;
    private const long InitialRevision = 0;
    private static readonly byte[] Payload = [0x31, 0x32, 0x33];

    [Test]
    public async Task AcBlob007UnknownStateFormatFailsWithoutOutcomeClockOrPositionEffects()
    {
        using var database = new TestDatabase();
        BlobStorageTestSupport.Configure(database);
        var blob = BlobStorageTestSupport.Blob(database, BlobId);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, blob, uploadId, Payload.Length, InitialRevision);
        ReplaceFormatVersion(database, StateKey(blob, uploadId));

        var operations = new BlobStorageOperations(database.Database);
        var commandId = Guid.NewGuid();
        var evaluatedAt = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var clockBefore = Read(database, KeySpace.Clock.ToArray());
        var positionBefore = database.Store.Position;
        var appliedBefore = database.Database.LastApplied;
        var command = new ReclaimBlobRequest(commandId, blob, uploadId, PartCount);

        var exception = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Apply(
            new(commandId, OperationKind.ReclaimBlob, PrincipalId, evaluatedAt,
                System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options))));

        await Assert.That(exception.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Read(database, KeySpace.Outcome(PrincipalId, commandId))).IsNull();
        await Assert.That(Read(database, KeySpace.Clock.ToArray()) is { } clockAfter
            && clockBefore is not null && clockAfter.AsSpan().SequenceEqual(clockBefore)).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(positionBefore);
        await Assert.That(database.Database.LastApplied).IsEqualTo(appliedBefore);
        var readFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            operations.UploadInfo(PrincipalId, new(blob, uploadId)));
        await Assert.That(readFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
    }

    [Test]
    public async Task AcBlob007UnknownPartMetadataFormatFailsBeforeReclaimEffects()
    {
        using var database = new TestDatabase();
        BlobStorageTestSupport.Configure(database);
        var blob = BlobStorageTestSupport.Blob(database, BlobId);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, blob, uploadId, Payload.Length, InitialRevision);
        BlobStorageTestSupport.Write(database, blob, uploadId, PartOrdinal, Payload);
        var integrity = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, Payload.Length);
        integrity = BlobIntegrity.NextHash(integrity, PartOrdinal, Payload.Length, BlobIntegrity.PartHash(Payload));
        var metadata = BlobStorageTestSupport.Complete(database, blob, uploadId, integrity).Value;
        var deleteId = Guid.NewGuid();
        database.Submit(OperationKind.DeleteBlob,
            new DeleteBlobRequest(deleteId, blob, metadata.Revision), id: deleteId).Get<BlobCommitResult<BlobMetadata>>();
        ReplaceFormatVersion(database, PartMetadataKey(blob, uploadId, PartOrdinal));

        var commandId = Guid.NewGuid();
        var clockBefore = Read(database, KeySpace.Clock.ToArray());
        var positionBefore = database.Store.Position;
        var appliedBefore = database.Database.LastApplied;
        var command = new ReclaimBlobRequest(commandId, blob, uploadId, PartCount);

        var exception = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Apply(
            new(commandId, OperationKind.ReclaimBlob, PrincipalId, TimeProvider.System.GetUtcNow().AddSeconds(1),
                System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options))));

        await Assert.That(exception.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Read(database, KeySpace.Outcome(PrincipalId, commandId))).IsNull();
        await Assert.That(Read(database, KeySpace.Clock.ToArray()) is { } clockAfter
            && clockBefore is not null && clockAfter.AsSpan().SequenceEqual(clockBefore)).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(positionBefore);
        await Assert.That(database.Database.LastApplied).IsEqualTo(appliedBefore);
        await Assert.That(Read(database, PartMetadataKey(blob, uploadId, PartOrdinal))).IsNotNull();
        await Assert.That(Read(database, PartKey(blob, uploadId, PartOrdinal))).IsNotNull();
    }

    private static byte[] StateKey(BlobRef blob, Guid uploadId) =>
        KeySpace.Partition(StateSpace, blob.Partition, blob.Resource, blob.Id, uploadId.ToString(GuidFormat));

    private static byte[] PartMetadataKey(BlobRef blob, Guid uploadId, int ordinal) =>
        KeySpace.Partition(PartMetadataSpace, blob.Partition, blob.Resource, blob.Id,
            uploadId.ToString(GuidFormat), (long)ordinal);

    private static byte[] PartKey(BlobRef blob, Guid uploadId, int ordinal) =>
        KeySpace.Partition(PartSpace, blob.Partition, blob.Resource, blob.Id,
            uploadId.ToString(GuidFormat), (long)ordinal);

    private static byte[]? Read(TestDatabase database, byte[] key) =>
        database.Store.Read(view => view.ReadOwnedValue(key));

    private static void ReplaceFormatVersion(TestDatabase database, byte[] key)
    {
        var serialized = Read(database, key) ?? throw new InvalidOperationException("Expected persisted blob record.");
        var record = JsonNode.Parse(serialized)?.AsObject()
            ?? throw new InvalidOperationException("Expected persisted JSON object.");
        record[FormatVersionProperty] = UnknownFormatVersion;
        var changed = Encoding.UTF8.GetBytes(record.ToJsonString(JsonDefaults.Options));
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, changed);
            return true;
        });
    }
}

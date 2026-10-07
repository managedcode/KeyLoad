using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobCurrentFormatOperations
{
    internal static async Task RejectDefaultLimitAsync(TestDatabase database, BlobRef blob)
    {
        var id = Guid.NewGuid();
        var upload = Guid.NewGuid();
        var request = new BeginBlobUploadRequest(id, blob, upload,
            BlobCurrentFormatFixture.DefaultMaxBlobBytes + BlobCurrentFormatFixture.ExceededBy, BlobCurrentFormatFixture.InitialRevision);
        var rejected = database.Submit(OperationKind.BeginBlobUpload, request, id: id);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(rejected.SafeDetail).IsEqualTo(BlobCurrentFormatFixture.InvalidDetail);
        var operations = new BlobStorageOperations(database.Database);
        await Assert.That(operations.Metadata(BlobCurrentFormatFixture.Principal, new(blob))).IsNull();
        await Assert.That(operations.UploadInfo(BlobCurrentFormatFixture.Principal, new(blob, upload))).IsNull();
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var replay = database.Submit(OperationKind.BeginBlobUpload, request, id: id);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(rejected))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task<(CompleteBlobUploadRequest Request, BlobCommitResult<BlobMetadata> Result, BlobMetadata Metadata)>
        PublishAsync(TestDatabase database, BlobRef blob)
    {
        var upload = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, blob, upload, BlobCurrentFormatFixture.Bytes.Length, BlobCurrentFormatFixture.InitialRevision);
        var written = BlobStorageTestSupport.Write(database, blob, upload, BlobCurrentFormatFixture.PartOrdinal, BlobCurrentFormatFixture.Bytes);
        var id = Guid.NewGuid();
        var integrity = BlobIntegrity.NextHash(BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, upload,
            BlobCurrentFormatFixture.Bytes.Length), BlobCurrentFormatFixture.PartOrdinal, BlobCurrentFormatFixture.Bytes.Length,
            BlobIntegrity.PartHash(BlobCurrentFormatFixture.Bytes));
        await Assert.That(written.Value.IntegrityHash).IsEqualTo(integrity);
        var request = new CompleteBlobUploadRequest(id, blob, upload, integrity);
        var now = database.Database.EvaluationClock.GetUtcNow();
        var result = database.Submit(OperationKind.CompleteBlobUpload, request, id: id, time: now).Get<BlobCommitResult<BlobMetadata>>();
        var expected = new BlobMetadata(blob, BlobCurrentFormatFixture.OriginalRevision, upload, BlobCurrentFormatFixture.Bytes.Length,
            BlobCurrentFormatFixture.PartCount, integrity, new RowAccess(), now);
        await Assert.That(NativeSerialization.Serialize(result.Value).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await Assert.That(result.Receipt.CommandId).IsEqualTo(id);
        await Assert.That(result.Receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(result.Receipt.Token.Position).IsEqualTo(database.Store.Position);
        await Assert.That(result.Receipt.Mutations).IsEmpty();
        await Assert.That(result.Receipt.Durability).IsEqualTo(database.Store.Identity.Durability);
        return (request, result, expected);
    }

    internal static async Task VerifyAsync(DatabaseEngine database, BlobMetadata expected)
    {
        var operations = new BlobStorageOperations(database);
        var metadata = operations.Metadata(BlobCurrentFormatFixture.Principal, new(expected.Blob))
            ?? throw new InvalidOperationException(BlobCurrentFormatFixture.BlobId);
        await Assert.That(NativeSerialization.Serialize(metadata).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        var partial = operations.Read(BlobCurrentFormatFixture.Principal,
            new(expected.Blob, expected.Revision, BlobCurrentFormatFixture.Offset, BlobCurrentFormatFixture.Count));
        var expectedPartial = new BlobReadResult(expected, BlobCurrentFormatFixture.Offset,
            System.Text.Encoding.UTF8.GetBytes(BlobCurrentFormatFixture.PartialPayload));
        await Assert.That(NativeSerialization.Serialize(partial).SequenceEqual(NativeSerialization.Serialize(expectedPartial))).IsTrue();
        var full = operations.Read(BlobCurrentFormatFixture.Principal, new(expected.Blob, expected.Revision,
            BlobCurrentFormatFixture.InitialRevision, BlobCurrentFormatFixture.Bytes.Length));
        var expectedFull = new BlobReadResult(expected, BlobCurrentFormatFixture.InitialRevision, BlobCurrentFormatFixture.Bytes);
        await Assert.That(NativeSerialization.Serialize(full).SequenceEqual(NativeSerialization.Serialize(expectedFull))).IsTrue();
    }
}

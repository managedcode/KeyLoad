using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobRangeIntegrityOperations
{
    internal static async Task<BlobRangeIntegrityFixture> Publish(TestDatabase database)
    {
        var resource = new ResourceDefinition(BlobRangeIntegrityFixture.Resource, ResourceKind.BlobStore,
            database.Partition.TransactionDomainId);
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
        var blob = new BlobRef(database.Partition, BlobRangeIntegrityFixture.Resource, BlobRangeIntegrityFixture.BlobId);
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload,
            BlobRangeIntegrityFixture.PayloadBytes, BlobRangeIntegrityFixture.InitialRevision);
        var beganAt = database.Database.EvaluationClock.GetUtcNow();
        var initialHash = BlobRangeIntegrityOracle.Initial(database.Store.Identity.Incarnation, blob, upload);
        var begun = Send(database.Database, OperationKind.BeginBlobUpload, begin, begin.CommandId, beganAt);
        await BlobWholeFlowAssertions.Receipt(database.Database, begun.Original.Get<BlobCommitResult<BlobUploadInfo>>().Receipt,
            begin.CommandId, blob);
        var expected = new BlobUploadInfo(blob, upload, BlobRangeIntegrityFixture.PayloadBytes,
            BlobRangeIntegrityFixture.InitialRevision, BlobRangeIntegrityFixture.EmptyCount, BlobRangeIntegrityFixture.EmptyCount,
            beganAt.AddSeconds(BlobRangeIntegrityFixture.DefaultUploadTtlSeconds), BlobUploadStatus.Active, initialHash);
        await BlobWholeFlowAssertions.Exact(begun.Original.Get<BlobCommitResult<BlobUploadInfo>>().Value, expected);
        var firstBytes = BlobRangeIntegrityFixture.Payload.AsMemory(BlobRangeIntegrityFixture.FirstOrdinal,
            BlobRangeIntegrityFixture.RawPartBytes);
        var first = await Write(database.Database, blob, upload, BlobRangeIntegrityFixture.FirstOrdinal, firstBytes);
        var firstHash = BlobRangeIntegrityOracle.Next(initialHash, BlobRangeIntegrityFixture.FirstOrdinal, firstBytes.Span);
        await BlobWholeFlowAssertions.Exact(first.Original.Get<BlobCommitResult<BlobUploadInfo>>().Value,
            expected with
            {
                NextOrdinal = BlobRangeIntegrityFixture.SingleCount,
                StoredBytes = firstBytes.Length,
                IntegrityHash = firstHash
            });
        var tailBytes = BlobRangeIntegrityFixture.Payload.AsMemory(BlobRangeIntegrityFixture.RawPartBytes);
        var second = await Write(database.Database, blob, upload, BlobRangeIntegrityFixture.SecondOrdinal, tailBytes);
        var hash = BlobRangeIntegrityOracle.Next(firstHash, BlobRangeIntegrityFixture.SecondOrdinal, tailBytes.Span);
        await BlobWholeFlowAssertions.Exact(second.Original.Get<BlobCommitResult<BlobUploadInfo>>().Value,
            expected with
            {
                NextOrdinal = BlobRangeIntegrityFixture.Parts,
                StoredBytes = BlobRangeIntegrityFixture.PayloadBytes,
                IntegrityHash = hash
            });
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var publishedAt = database.Database.EvaluationClock.GetUtcNow();
        var publication = Send(database.Database, OperationKind.CompleteBlobUpload, complete, complete.CommandId, publishedAt);
        await BlobWholeFlowAssertions.Receipt(database.Database, publication.Original.Get<BlobCommitResult<BlobMetadata>>().Receipt,
            complete.CommandId, blob);
        var metadata = new BlobMetadata(blob, BlobRangeIntegrityFixture.Revision, upload,
            BlobRangeIntegrityFixture.PayloadBytes, BlobRangeIntegrityFixture.Parts, hash, new RowAccess(), publishedAt);
        await BlobWholeFlowAssertions.Exact(publication.Original.Get<BlobCommitResult<BlobMetadata>>().Value, metadata);
        var fixture = new BlobRangeIntegrityFixture(blob, upload, begin.CommandId, beganAt, metadata,
            [begun, first, second, publication]);
        await BlobRangeIntegrityAssertions.NativeState(database.Database, fixture);
        return fixture;
    }

    private static async Task<BlobRangeRetainedOperation> Write(DatabaseEngine database, BlobRef blob, Guid upload,
        int ordinal, ReadOnlyMemory<byte> bytes)
    {
        var request = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, ordinal, bytes, BlobRangeIntegrityOracle.Hash(bytes.Span));
        var command = Send(database, OperationKind.WriteBlobPart, request, request.CommandId, database.EvaluationClock.GetUtcNow());
        await BlobWholeFlowAssertions.Receipt(database, command.Original.Get<BlobCommitResult<BlobUploadInfo>>().Receipt,
            request.CommandId, blob);
        return command;
    }

    private static BlobRangeRetainedOperation Send<T>(DatabaseEngine database, OperationKind kind, T payload,
        Guid command, DateTimeOffset now)
    {
        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        var result = database.Apply(database.CreateCommand(kind, command, BlobRangeIntegrityFixture.Principal, now, json));
        return new(kind, command, json, result);
    }
}

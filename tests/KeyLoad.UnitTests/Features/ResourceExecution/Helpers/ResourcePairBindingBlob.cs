using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.BlobStorage;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingBlob
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var source = state.Source;
        var blob = new BlobRef(source.Partition, Blobs, BlobId);
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, BlobBytes, InitialRevision);
        var initial = Initial(source.Store.Identity.Incarnation, blob, upload);
        var beginIndex = source.ReadJournalCut().LastIndex + One;
        var begun = Send(source, OperationKind.BeginBlobUpload, begin.CommandId, begin, token);
        var began = source.ReadRetainedEntry(begin.CommandId).Operation!;
        var expected = new BlobUploadInfo(blob, upload, BlobBytes, InitialRevision, Zero, Zero,
            began.EvaluatedAt.AddSeconds(BlobLimits.DefaultUploadTtlSeconds), BlobUploadStatus.Active, initial);
        await CheckAsync(state, began, begun, expected, beginIndex);
        var write = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, Zero, Payload, BlobRangeIntegrityOracle.Hash(Payload));
        var issued = source.Database.CreateNativeOperation(OperationKind.WriteBlobPart, write.CommandId, Root,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(write));
        var writeIndex = source.ReadJournalCut().LastIndex + One;
        var applied = await ResourcePairBindingRecovery.ApplyAsync(state, issued, Blobs, token);
        var hash = BlobRangeIntegrityOracle.Next(initial, Zero, Payload);
        await CheckAsync(state, applied.Original, applied.Result, expected with
        {
            NextOrdinal = One,
            StoredBytes = BlobBytes,
            IntegrityHash = hash
        }, writeIndex, false);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completeIndex = source.ReadJournalCut().LastIndex + One;
        var published = Send(source, OperationKind.CompleteBlobUpload, complete.CommandId, complete, token);
        var publication = source.ReadRetainedEntry(complete.CommandId).Operation!;
        var metadata = new BlobMetadata(blob, FirstRevision, upload, BlobBytes, One, hash, new RowAccess(), publication.EvaluatedAt);
        await CheckAsync(state, publication, published, metadata, completeIndex);
        state.BlobHeads.Add(metadata);
        await HealthyAsync(state, source.Database);
    }

    private static OperationResult Send<T>(TestDatabase source, OperationKind kind, Guid id, T payload, CancellationToken token)
        => source.SubmitIssuedEmbedded(source.Database.CreateNativeOperation(kind, id, Root,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload)), token);

    private static async Task CheckAsync<T>(ResourcePairBindingState state, ReplicatedOperation original, OperationResult result, T value, long expectedIndex, bool remember = true)
    {
        var receipt = ResourcePairBindingAssertions.Receipt(state.Source.Store, state.Source.Partition, original.Id,
            expectedIndex, []);
        await Assert.That(state.Source.ReadRetainedEntry(original).Index).IsEqualTo(expectedIndex);
        await ResourcePairBindingAssertions.EqualAsync(new BlobCommitResult<T>(receipt, value), result.Get<BlobCommitResult<T>>());
        if (remember)
        {
            ResourcePairBindingAssertions.Remember(state, original, result);
        }
    }

    internal static async Task HealthyAsync(ResourcePairBindingState state, DatabaseEngine database)
    {
        var operations = new BlobStorageOperations(database);
        foreach (var metadata in state.BlobHeads)
        {
            await ResourcePairBindingAssertions.EqualAsync(metadata, operations.Metadata(Root, new(metadata.Blob))!);
            var range = operations.Read(Root, new(metadata.Blob, FirstRevision, Zero, BlobBytes));
            await ResourcePairBindingAssertions.EqualAsync(metadata, range.Metadata);
            await Assert.That(range.Offset).IsEqualTo((long)Zero);
            await Assert.That(range.Bytes.Span.SequenceEqual(Payload)).IsTrue();
        }
    }

    private static string Initial(Guid incarnation, BlobRef blob, Guid upload)
        => Convert.ToHexStringLower(SHA256.HashData(KeyCodec.Encode(ChainDomain, incarnation.ToString(GuidFormat),
            blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Partition.PartitionKey,
            blob.Resource, blob.Id, upload.ToString(GuidFormat), (long)BlobBytes, (long)BlobLimits.RawPartBytes)));
}

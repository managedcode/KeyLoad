using System.Security.Cryptography;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.BlobStorage;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingState;

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
        var beginIndex = source.Database.LastApplied + One;
        var begun = Send(source, OperationKind.BeginBlobUpload, begin.CommandId, begin, token);
        var began = source.ReadStoredOutcome(begin.CommandId).PreparedCommand;
        var expected = new BlobUploadInfo(blob, upload, BlobBytes, InitialRevision, Zero, Zero,
            new DateTimeOffset(began.Timestamp).AddSeconds(BlobLimits.DefaultUploadTtlSeconds), BlobUploadStatus.Active, initial);
        await CheckAsync(state, began, begun, expected, beginIndex);
        var write = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, Zero, Payload, BlobRangeIntegrityOracle.Hash(Payload));
        var issued = source.Database.CreateCommand(OperationKind.WriteBlobPart, write.CommandId, Root,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(write));
        var writeIndex = source.Database.LastApplied + One;
        var applied = await ResourcePairBindingFlow.ApplyAfterRepairAsync(state, issued, Blobs, token);
        var hash = BlobRangeIntegrityOracle.Next(initial, Zero, Payload);
        await CheckAsync(state, applied.Original, applied.Result, expected with
        {
            NextOrdinal = One,
            StoredBytes = BlobBytes,
            IntegrityHash = hash
        }, writeIndex, false);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completeIndex = source.Database.LastApplied + One;
        var published = Send(source, OperationKind.CompleteBlobUpload, complete.CommandId, complete, token);
        var publication = source.ReadStoredOutcome(complete.CommandId).PreparedCommand;
        var metadata = new BlobMetadata(blob, FirstRevision, upload, BlobBytes, One, hash, new RowAccess(), new DateTimeOffset(publication.Timestamp));
        await CheckAsync(state, publication, published, metadata, completeIndex);
        state.BlobHeads.Add(metadata);
        await HealthyAsync(state, source.Database);
    }

    private static Result<object> Send<T>(TestDatabase source, OperationKind kind, Guid id, T payload, CancellationToken token)
        => source.SubmitIssuedEmbedded(source.Database.CreateCommand(kind, id, Root,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload)), token);

    private static async Task CheckAsync<T>(ResourcePairBindingState state, Command<DatabaseCommand> original,
        Result<object> result, T value, long expectedIndex, bool remember = true)
    {
        var receipt = ResourcePairBindingAssertions.Receipt(state.Source.Store, state.Source.Partition, original.CommandId,
            expectedIndex, []);
        await Assert.That(state.Source.ReadStoredOutcome(original).CanonicalSequence).IsEqualTo(expectedIndex);
        await ResourceIncarnationAssertions.EqualAsync(new BlobCommitResult<T>(receipt, value), result.Get<BlobCommitResult<T>>());
        if (remember)
        {
            state.Remember(original, result);
        }
    }

    internal static async Task HealthyAsync(ResourcePairBindingState state, DatabaseEngine database)
    {
        var operations = new BlobStorageOperations(database);
        foreach (var metadata in state.BlobHeads)
        {
            await ResourceIncarnationAssertions.EqualAsync(metadata, operations.Metadata(Root, new(metadata.Blob))!);
            var range = operations.Read(Root, new(metadata.Blob, FirstRevision, Zero, BlobBytes));
            await ResourceIncarnationAssertions.EqualAsync(metadata, range.Metadata);
            await Assert.That(range.Offset).IsEqualTo((long)Zero);
            await Assert.That(range.Bytes.Span.SequenceEqual(Payload)).IsTrue();
        }
    }

    private static string Initial(Guid incarnation, BlobRef blob, Guid upload)
        => Convert.ToHexStringLower(SHA256.HashData(KeyCodec.Encode(ChainDomain, incarnation.ToString(GuidFormat),
            blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Partition.PartitionKey,
            blob.Resource, blob.Id, upload.ToString(GuidFormat), (long)BlobBytes, (long)BlobLimits.RawPartBytes)));
}

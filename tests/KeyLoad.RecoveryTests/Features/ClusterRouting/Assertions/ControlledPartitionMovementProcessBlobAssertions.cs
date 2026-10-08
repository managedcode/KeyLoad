using System.Text.Json;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Checks complete literal current-format blob values without binary graph identity assumptions.</summary>
internal static class ControlledPartitionMovementProcessBlobAssertions
{
    private const long PublishedRevision = 1;
    private const int PublishedPartCount = 1;
    private const int FullOffset = 0;
    private const int PartialOffset = 1;
    private const int PartialLength = 2;

    internal static async Task<BlobMetadata> PublishedAsync(OperationResult result,
        CompleteBlobUploadRequest original, DateTimeOffset evaluatedAt, CommitToken expectedToken,
        DurabilityProfile expectedDurability)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        var published = result.Get<BlobCommitResult<BlobMetadata>>();
        var bytes = ControlledPartitionMovementProcessLiteralCorpus.BlobBytes();
        var integrity = ControlledPartitionMovementProcessLiteralCorpus.BlobIntegrityHash(expectedToken.Incarnation);
        await Assert.That(original.CommandId).IsEqualTo(ControlledPartitionMovementProcessLiteralCorpus.BlobCompleteId);
        await Assert.That(original.Blob).IsEqualTo(ControlledPartitionMovementProcessLiteralCorpus.Blob);
        await Assert.That(original.UploadId).IsEqualTo(ControlledPartitionMovementProcessLiteralCorpus.BlobUploadId);
        await Assert.That(original.ExpectedIntegrityHash).IsEqualTo(integrity);
        var metadata = new BlobMetadata(ControlledPartitionMovementProcessLiteralCorpus.Blob, PublishedRevision,
            ControlledPartitionMovementProcessLiteralCorpus.BlobUploadId, bytes.Length, PublishedPartCount,
            integrity, new RowAccess(), evaluatedAt, false);
        var receipt = new CommitReceipt(ControlledPartitionMovementProcessLiteralCorpus.BlobCompleteId,
            expectedToken, [], expectedDurability);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(published, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(new BlobCommitResult<BlobMetadata>(receipt, metadata), JsonDefaults.Options))).IsTrue();
        return metadata;
    }

    internal static async Task ReadAsync(ControlledPartitionMovementNativeNode node, BlobMetadata expected,
        CancellationToken cancellationToken)
    {
        var operations = new BlobStorageOperations(node.Database);
        var metadata = operations.Metadata(ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            new(expected.Blob), cancellationToken);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(metadata, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
        var full = operations.Read(ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            new(expected.Blob, PublishedRevision, FullOffset, ControlledPartitionMovementProcessLiteralCorpus.BlobBytes().Length), cancellationToken);
        var expectedFull = new BlobReadResult(expected, FullOffset, ControlledPartitionMovementProcessLiteralCorpus.BlobBytes());
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(full, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expectedFull, JsonDefaults.Options))).IsTrue();
        var partial = operations.Read(ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            new(expected.Blob, PublishedRevision, PartialOffset, PartialLength), cancellationToken);
        var expectedPartial = new BlobReadResult(expected, PartialOffset, ControlledPartitionMovementProcessLiteralCorpus.PartialBlobBytes());
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(partial, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expectedPartial, JsonDefaults.Options))).IsTrue();
    }

    internal static async Task ReplayAsync(ControlledPartitionMovementNativeNode source,
        CompleteBlobUploadRequest originalRequest, DateTimeOffset originalEvaluatedAt,
        OperationResult originalOutcome, CancellationToken cancellationToken)
    {
        var original = source.Database.CreateNativeOperation(OperationKind.CompleteBlobUpload,
            originalRequest.CommandId, ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            originalEvaluatedAt, NativeSerialization.Serialize(originalRequest));
        var result = source.Journal.Submit(original, cancellationToken);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        await Assert.That(NativeSerialization.Serialize(result).SequenceEqual(NativeSerialization.Serialize(originalOutcome))).IsTrue();
    }
}

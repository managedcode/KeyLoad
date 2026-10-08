using System.Text.Json;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks complete literal current-format blob values without binary graph identity assumptions.</summary>
internal static class ControlledPartitionMovementBlobAssertions
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
        var bytes = ControlledPartitionMovementBlobSeed.Bytes();
        var metadata = new BlobMetadata(ControlledPartitionMovementBlobSeed.Blob, PublishedRevision,
            ControlledPartitionMovementBlobSeed.UploadId, bytes.Length, PublishedPartCount,
            original.ExpectedIntegrityHash, new RowAccess(), evaluatedAt, false);
        var receipt = new CommitReceipt(ControlledPartitionMovementBlobSeed.CompleteId,
            expectedToken, [], expectedDurability);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(published, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(new BlobCommitResult<BlobMetadata>(receipt, metadata), JsonDefaults.Options))).IsTrue();
        return metadata;
    }

    internal static async Task ReadAsync(ControlledPartitionMovementNode node, BlobMetadata expected,
        CancellationToken cancellationToken)
    {
        var operations = new BlobStorageOperations(node.Database);
        var metadata = operations.Metadata(PhysicalShardCatalogFixture.RootPrincipalId,
            new(expected.Blob), cancellationToken);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(metadata, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
        var full = operations.Read(PhysicalShardCatalogFixture.RootPrincipalId,
            new(expected.Blob, PublishedRevision, FullOffset, ControlledPartitionMovementBlobSeed.Bytes().Length), cancellationToken);
        var expectedFull = new BlobReadResult(expected, FullOffset, ControlledPartitionMovementBlobSeed.Bytes());
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(full, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expectedFull, JsonDefaults.Options))).IsTrue();
        var partial = operations.Read(PhysicalShardCatalogFixture.RootPrincipalId,
            new(expected.Blob, PublishedRevision, PartialOffset, PartialLength), cancellationToken);
        var expectedPartial = new BlobReadResult(expected, PartialOffset, new byte[] { 2, 3 });
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(partial, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expectedPartial, JsonDefaults.Options))).IsTrue();
    }

    internal static async Task ReplayAsync(OperationResult result, byte[] originalOutcome)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(result).SequenceEqual(originalOutcome)).IsTrue();
    }
}

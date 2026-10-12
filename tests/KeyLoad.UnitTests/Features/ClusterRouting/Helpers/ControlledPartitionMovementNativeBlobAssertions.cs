using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains complete blob metadata, acknowledged receipt and literal full/partial bytes across cold replay.</summary>
internal static class ControlledPartitionMovementNativeBlobAssertions
{
    private const int FullOffset = 0;
    private const int PartialOffset = 1;
    private const int PartialLength = 2;

    internal static async Task ReplyAsync(GrainOperationReply reply, BlobCommitResult<BlobMetadata> expected)
    {
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as BlobCommitResult<BlobMetadata>;
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task ReadAsync(ControlledPartitionMovementNativeHost source, BlobMetadata expected,
        CancellationToken cancellationToken)
    {
        var operations = new BlobStorageOperations(source.Database);
        var metadata = operations.Metadata(PhysicalShardCatalogFixture.RootPrincipalId, new(expected.Blob), cancellationToken);
        await Assert.That(JsonDefaults.Serialize(metadata).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var full = operations.Read(PhysicalShardCatalogFixture.RootPrincipalId,
            new(expected.Blob, expected.Revision, FullOffset, ControlledPartitionMovementBlobSeed.Bytes().Length), cancellationToken);
        await Assert.That(JsonDefaults.Serialize(full).AsSpan().SequenceEqual(JsonDefaults.Serialize(
            new BlobReadResult(expected, FullOffset, ControlledPartitionMovementBlobSeed.Bytes())))).IsTrue();
        var partial = operations.Read(PhysicalShardCatalogFixture.RootPrincipalId,
            new(expected.Blob, expected.Revision, PartialOffset, PartialLength), cancellationToken);
        await Assert.That(JsonDefaults.Serialize(partial).AsSpan().SequenceEqual(JsonDefaults.Serialize(
            new BlobReadResult(expected, PartialOffset, new byte[] { 2, 3 })))).IsTrue();
    }

    internal static async Task ReplayAsync(ControlledPartitionMovementNativeHost source, CompleteBlobUploadRequest original,
        BlobCommitResult<BlobMetadata> expected, CancellationToken cancellationToken)
    {
        var reply = await source.SubmitAsync(OperationKind.CompleteBlobUpload, original.CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(original), cancellationToken);
        await ReplyAsync(reply, expected);
        await ReadAsync(source, expected.Value, cancellationToken);
    }
}

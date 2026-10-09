using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementBlobWireEffects
{
    private const string SecondaryId = "wire-secondary";
    private const int Length = 8;
    private const int FirstOrdinal = 0;
    private const int OnePart = 1;
    private const long InitialRevision = 0;
    private const long PublishedRevision = 1;
    private const long TombstoneRevision = 2;
    private const byte Content = 0x48;

    internal static async Task RequireAsync(PartitionMovementBlobWireCallers callers, PartitionMovementBlobWireSeed seed,
        CancellationToken token)
    {
        var blob = seed.Metadata.Blob with { Id = SecondaryId };
        var aborted = await UploadAsync(callers, seed.Partition, blob, false, token);
        var reclaim = new ReclaimBlobRequest(Guid.NewGuid(), blob, aborted, OnePart);
        var reclaimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.ReclaimBlobAsync(reclaim, token));
        await Assert.That(reclaimed.Value).IsEqualTo(new BlobReclaimResult(aborted, OnePart, FirstOrdinal, Length, true));
        await callers.RequireAsync(seed.Partition, BlobToolNames.Reclaim, reclaim,
            ct => callers.Source.ReclaimBlobAsync(reclaim, ct), reclaimed, token);
        var published = await UploadAsync(callers, seed.Partition, blob, true, token);
        var metadata = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.GetBlobMetadataAsync(new(blob), token));
        await Assert.That(metadata!.Length).IsEqualTo((long)Length);
        await Assert.That(metadata.Revision).IsEqualTo(PublishedRevision);
        var delete = new DeleteBlobRequest(Guid.NewGuid(), blob, metadata.Revision);
        var deleted = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.DeleteBlobAsync(delete, token));
        await Assert.That(deleted.Value.Deleted).IsTrue();
        await Assert.That(deleted.Value.Revision).IsEqualTo(TombstoneRevision);
        await Assert.That(deleted.Value.Length).IsEqualTo((long)FirstOrdinal);
        await Assert.That(deleted.Value.VersionId).IsNull();
        await SqlRf3Protocol.EqualAsync(metadata with { Revision = TombstoneRevision, VersionId = null,
            Length = InitialRevision, PartCount = FirstOrdinal, IntegrityHash = null, Deleted = true,
            UpdatedAt = deleted.Value.UpdatedAt }, deleted.Value);
        await callers.RequireAsync(seed.Partition, BlobToolNames.Delete, delete,
            ct => callers.Source.DeleteBlobAsync(delete, ct), deleted, token);
        var cleanup = new ReclaimBlobRequest(Guid.NewGuid(), blob, published, OnePart);
        var complete = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.ReclaimBlobAsync(cleanup, token));
        await Assert.That(complete.Value).IsEqualTo(new BlobReclaimResult(published, OnePart, FirstOrdinal, Length, true));
        await callers.RequireAsync(seed.Partition, BlobToolNames.Reclaim, cleanup,
            ct => callers.Source.ReclaimBlobAsync(cleanup, ct), complete, token);
        await seed.RequireAsync(token);
    }

    private static async Task<Guid> UploadAsync(PartitionMovementBlobWireCallers callers, PartitionRef partition,
        BlobRef blob, bool publish, CancellationToken token)
    {
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, Length, InitialRevision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.BeginBlobUploadAsync(begin, token));
        await callers.RequireAsync(partition, BlobToolNames.BeginUpload, begin,
            ct => callers.Source.BeginBlobUploadAsync(begin, ct), begun, token);
        var bytes = Enumerable.Repeat(Content, Length).ToArray();
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, FirstOrdinal, bytes, BlobIntegrity.PartHash(bytes));
        var written = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.WriteBlobPartAsync(part, token));
        await callers.RequireAsync(partition, BlobToolNames.WritePart, part,
            ct => callers.Source.WriteBlobPartAsync(part, ct), written, token);
        if (publish)
        {
            var command = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload,
                BlobIntegrity.NextHash(begun.Value.IntegrityHash, part.Ordinal, part.Bytes.Length, part.Sha256));
            var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.CompleteBlobUploadAsync(command, token));
            await callers.RequireAsync(partition, BlobToolNames.CompleteUpload, command,
                ct => callers.Source.CompleteBlobUploadAsync(command, ct), result, token);
        }
        else
        {
            var command = new AbortBlobUploadRequest(Guid.NewGuid(), blob, upload);
            var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.AbortBlobUploadAsync(command, token));
            await Assert.That(result.Value.Status).IsEqualTo(BlobUploadStatus.Aborted);
            await callers.RequireAsync(partition, BlobToolNames.AbortUpload, command,
                ct => callers.Source.AbortBlobUploadAsync(command, ct), result, token);
        }
        return upload;
    }
}

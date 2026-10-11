using KeyLoad.RecoveryTests.Features.Messaging;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobRetiredPartSeed
{
    internal static async Task<BlobRetiredPartState> CreateAsync(string root)
    {
        var publication = await BlobPriorPublicationCommands.SeedAsync(root);
        using var store = BlobPriorPublicationTrial.Open(root);
        var owner = QueueLifecycleRecoveryAssertions.Open(store);
        var request = new DeleteBlobRequest(Guid.NewGuid(), publication.Blob, 1);
        var deletion = BlobPriorPublicationCommands.Send(owner, OperationKind.DeleteBlob, request.CommandId, request);
        var tombstone = publication.Metadata with
        {
            Revision = 2,
            VersionId = null,
            Length = 0,
            PartCount = 0,
            IntegrityHash = null,
            UpdatedAt = deletion.Operation.EvaluatedAt,
            Deleted = true
        };
        await BlobPriorPublicationImage.ExactAsync(deletion.Result.Get<BlobCommitResult<BlobMetadata>>().Value, tombstone);
        await BlobPriorPublicationImage.ReceiptAsync(owner, deletion.Result.Get<BlobCommitResult<BlobMetadata>>().Receipt,
            request.CommandId, publication.Blob);
        var result = new BlobRetiredPartState(publication, deletion, tombstone);
        await BlobRetiredPartAssertions.HealthyAsync(owner, result);
        return result;
    }
}

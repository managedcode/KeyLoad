using System.Collections.Immutable;
using KeyLoad.Orleans;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Original blob corpus through the genuine signed native host with every original reply retained.</summary>
internal static class ControlledPartitionMovementNativeProcessBlob
{
    private const int FirstOrdinal = 0;
    private const int InitialRevision = 0;
    private const string PrincipalId = "root";
    private const int LatestReplyOffset = 1;
    private const string ConfigureCommandId = "a3aa5b55-c605-4c3b-9e49-871b876ef608";
    private const string BeginCommandId = "28175434-9a57-434e-b201-b376c9206f2d";
    private const string PartCommandId = "5738caef-a9e4-4623-ad93-d91e3838aff6";
    private static readonly Guid ConfigureId = Guid.Parse(ConfigureCommandId);
    private static readonly Guid BeginId = Guid.Parse(BeginCommandId);
    private static readonly Guid PartId = Guid.Parse(PartCommandId);

    internal static async Task<(CompleteBlobUploadRequest? Request, ImmutableArray<GrainOperationReply> Replies)> PublishAsync(
        ControlledPartitionMovementNativeHost source, CancellationToken cancellationToken)
    {
        var replies = ImmutableArray.CreateBuilder<GrainOperationReply>();
        var partition = ControlledPartitionMovementProcessCorpus.Partition;
        await SubmitAsync(source, OperationKind.ConfigureResource, ConfigureId,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(ControlledPartitionMovementProcessBlobSeed.Resource, ResourceKind.BlobStore,
                    partition.TransactionDomainId)), replies, cancellationToken);
        if (replies[^LatestReplyOffset].Error is not null)
        { return (null, replies.ToImmutable()); }
        var bytes = ControlledPartitionMovementProcessBlobSeed.Bytes();
        await SubmitAsync(source, OperationKind.BeginBlobUpload, BeginId,
            new BeginBlobUploadRequest(BeginId, ControlledPartitionMovementProcessBlobSeed.Blob,
                ControlledPartitionMovementProcessBlobSeed.UploadId, bytes.Length, InitialRevision), replies, cancellationToken);
        if (replies[^LatestReplyOffset].Error is not null)
        { return (null, replies.ToImmutable()); }
        await SubmitAsync(source, OperationKind.WriteBlobPart, PartId,
            new WriteBlobPartRequest(PartId, ControlledPartitionMovementProcessBlobSeed.Blob,
                ControlledPartitionMovementProcessBlobSeed.UploadId, FirstOrdinal, bytes,
                ControlledPartitionMovementProcessBlobSeed.PartSha256), replies, cancellationToken);
        if (replies[^LatestReplyOffset].Error is not null)
        { return (null, replies.ToImmutable()); }
        var initial = BlobIntegrity.InitialHash(source.Store.Identity.Incarnation,
            ControlledPartitionMovementProcessBlobSeed.Blob, ControlledPartitionMovementProcessBlobSeed.UploadId, bytes.Length);
        var integrity = BlobIntegrity.NextHash(initial, FirstOrdinal, bytes.Length,
            ControlledPartitionMovementProcessBlobSeed.PartSha256);
        var request = new CompleteBlobUploadRequest(ControlledPartitionMovementProcessBlobSeed.CompleteId,
            ControlledPartitionMovementProcessBlobSeed.Blob, ControlledPartitionMovementProcessBlobSeed.UploadId, integrity);
        await SubmitAsync(source, OperationKind.CompleteBlobUpload, request.CommandId, request, replies, cancellationToken);
        return (request, replies.ToImmutable());
    }

    private static async Task SubmitAsync<T>(ControlledPartitionMovementNativeHost source, OperationKind kind,
        Guid commandId, T request, ImmutableArray<GrainOperationReply>.Builder replies, CancellationToken cancellationToken)
        => replies.Add(await source.SubmitAsync(kind, commandId, PrincipalId,
            NativeSerialization.Serialize(request), cancellationToken).ConfigureAwait(false));
}

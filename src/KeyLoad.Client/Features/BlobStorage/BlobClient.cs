using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Reads authorized current metadata, including null for an absent object.</summary>
    public Task<Result<BlobMetadata?>> GetBlobMetadataAsync(BlobMetadataRequest request, CancellationToken cancellationToken = default)
        => Send<BlobMetadata?>(BlobOperationProtocol.Metadata, request, false, null, cancellationToken);

    /// <summary>Lists a bounded page of visible live blob metadata.</summary>
    public Task<Result<BlobListPage>> ListBlobsAsync(BlobListRequest request, CancellationToken cancellationToken = default)
        => Send<BlobListPage>(BlobOperationProtocol.List, request, false, null, cancellationToken);

    /// <summary>Reads an exact bounded byte range at the required current revision.</summary>
    public Task<Result<BlobReadResult>> ReadBlobRangeAsync(BlobReadRequest request, CancellationToken cancellationToken = default)
        => Send<BlobReadResult>(BlobOperationProtocol.ReadRange, request, false, null, cancellationToken);

    /// <summary>Reads creator-scoped upload progress, including null when absent.</summary>
    public Task<Result<BlobUploadInfo?>> GetBlobUploadInfoAsync(BlobUploadInfoRequest request, CancellationToken cancellationToken = default)
        => Send<BlobUploadInfo?>(BlobOperationProtocol.UploadInfo, request, false, null, cancellationToken);

    /// <summary>Starts one scoped upload with the caller's stable command identity.</summary>
    public Task<Result<BlobCommitResult<BlobUploadInfo>>> BeginBlobUploadAsync(BeginBlobUploadRequest request, CancellationToken cancellationToken = default)
        => Send<BlobCommitResult<BlobUploadInfo>>(BlobOperationProtocol.BeginUpload, request, true, request.CommandId, cancellationToken);

    /// <summary>Writes or identically retries one bounded raw part.</summary>
    public Task<Result<BlobCommitResult<BlobUploadInfo>>> WriteBlobPartAsync(WriteBlobPartRequest request, CancellationToken cancellationToken = default)
        => Send<BlobCommitResult<BlobUploadInfo>>(BlobOperationProtocol.WritePart, request, true, request.CommandId, cancellationToken);

    /// <summary>Publishes a complete upload after integrity and revision checks.</summary>
    public Task<Result<BlobCommitResult<BlobMetadata>>> CompleteBlobUploadAsync(CompleteBlobUploadRequest request, CancellationToken cancellationToken = default)
        => Send<BlobCommitResult<BlobMetadata>>(BlobOperationProtocol.CompleteUpload, request, true, request.CommandId, cancellationToken);

    /// <summary>Aborts an upload with the caller's stable command identity.</summary>
    public Task<Result<BlobCommitResult<BlobUploadInfo>>> AbortBlobUploadAsync(AbortBlobUploadRequest request, CancellationToken cancellationToken = default)
        => Send<BlobCommitResult<BlobUploadInfo>>(BlobOperationProtocol.AbortUpload, request, true, request.CommandId, cancellationToken);

    /// <summary>Deletes the required current blob revision.</summary>
    public Task<Result<BlobCommitResult<BlobMetadata>>> DeleteBlobAsync(DeleteBlobRequest request, CancellationToken cancellationToken = default)
        => Send<BlobCommitResult<BlobMetadata>>(BlobOperationProtocol.Delete, request, true, request.CommandId, cancellationToken);

    /// <summary>Reclaims bounded retired or aborted parts.</summary>
    public Task<Result<BlobCommitResult<BlobReclaimResult>>> ReclaimBlobAsync(ReclaimBlobRequest request, CancellationToken cancellationToken = default)
        => Send<BlobCommitResult<BlobReclaimResult>>(BlobOperationProtocol.Reclaim, request, true, request.CommandId, cancellationToken);
}

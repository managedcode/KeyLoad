using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Typed blob operations sharing the client's canonical authenticated transport.</summary>
public static class BlobClientExtensions
{
    /// <summary>Reads authorized current metadata, including null for an absent object.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The complete blob identity.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The authorized metadata, including null when absent, or a classified problem.</returns>
    public static Task<Result<BlobMetadata?>> GetBlobMetadataAsync(this KeyLoadClient client, BlobMetadataRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobMetadata?>(BlobOperationProtocol.Metadata, request, false, null, cancellationToken);
    }

    /// <summary>Lists a bounded page of visible live blob metadata.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The resource identity and bounded page controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The authorized metadata page or a classified problem.</returns>
    public static Task<Result<BlobListPage>> ListBlobsAsync(this KeyLoadClient client, BlobListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobListPage>(BlobOperationProtocol.List, request, false, null, cancellationToken);
    }

    /// <summary>Reads an exact bounded byte range at the required current revision.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The required revision and exact byte range.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The metadata and exact bytes or a classified problem.</returns>
    public static Task<Result<BlobReadResult>> ReadBlobRangeAsync(this KeyLoadClient client, BlobReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobReadResult>(BlobOperationProtocol.ReadRange, request, false, null, cancellationToken);
    }

    /// <summary>Reads creator-scoped upload progress, including null when absent.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The complete blob and upload identities.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The upload progress, including null when absent, or a classified problem.</returns>
    public static Task<Result<BlobUploadInfo?>> GetBlobUploadInfoAsync(this KeyLoadClient client, BlobUploadInfoRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobUploadInfo?>(BlobOperationProtocol.UploadInfo, request, false, null, cancellationToken);
    }

    /// <summary>Starts one scoped upload with the caller's stable command identity.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The upload reservation, revision and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed upload reservation or a classified problem.</returns>
    public static Task<Result<BlobCommitResult<BlobUploadInfo>>> BeginBlobUploadAsync(this KeyLoadClient client, BeginBlobUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobCommitResult<BlobUploadInfo>>(BlobOperationProtocol.BeginUpload, request, true, request.CommandId, cancellationToken);
    }

    /// <summary>Writes or identically retries one bounded raw part.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The ordered part, integrity hash and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed upload progress or a classified problem.</returns>
    public static Task<Result<BlobCommitResult<BlobUploadInfo>>> WriteBlobPartAsync(this KeyLoadClient client, WriteBlobPartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobCommitResult<BlobUploadInfo>>(BlobOperationProtocol.WritePart, request, true, request.CommandId, cancellationToken);
    }

    /// <summary>Publishes a complete upload after integrity and revision checks.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The upload identity, final integrity hash and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed publication metadata or a classified problem.</returns>
    public static Task<Result<BlobCommitResult<BlobMetadata>>> CompleteBlobUploadAsync(this KeyLoadClient client, CompleteBlobUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobCommitResult<BlobMetadata>>(BlobOperationProtocol.CompleteUpload, request, true, request.CommandId, cancellationToken);
    }

    /// <summary>Aborts an upload with the caller's stable command identity.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The upload identity and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed aborted upload state or a classified problem.</returns>
    public static Task<Result<BlobCommitResult<BlobUploadInfo>>> AbortBlobUploadAsync(this KeyLoadClient client, AbortBlobUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobCommitResult<BlobUploadInfo>>(BlobOperationProtocol.AbortUpload, request, true, request.CommandId, cancellationToken);
    }

    /// <summary>Deletes the required current blob revision.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The required blob revision and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed revision tombstone or a classified problem.</returns>
    public static Task<Result<BlobCommitResult<BlobMetadata>>> DeleteBlobAsync(this KeyLoadClient client, DeleteBlobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobCommitResult<BlobMetadata>>(BlobOperationProtocol.Delete, request, true, request.CommandId, cancellationToken);
    }

    /// <summary>Reclaims bounded retired or aborted parts.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">The selected version, reclaim bound and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed bounded reclaim progress or a classified problem.</returns>
    public static Task<Result<BlobCommitResult<BlobReclaimResult>>> ReclaimBlobAsync(this KeyLoadClient client, ReclaimBlobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<BlobCommitResult<BlobReclaimResult>>(BlobOperationProtocol.Reclaim, request, true, request.CommandId, cancellationToken);
    }
}

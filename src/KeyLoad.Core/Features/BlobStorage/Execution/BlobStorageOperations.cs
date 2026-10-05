using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

/// <summary>Executes bounded canonical blob operations using borrowed node-local engine ownership.</summary>
/// <param name="database">The engine that owns shared command and authorization semantics.</param>
/// <param name="timeProvider">Optional business clock for gated read authority.</param>
public sealed class BlobStorageOperations(DatabaseEngine database, TimeProvider? timeProvider = null)
{
    private BlobReads Reads => new(database, timeProvider ?? database.EvaluationClock);

    /// <summary>Identifies the six canonical blob mutation kinds.</summary>
    /// <param name="kind">The operation kind to inspect.</param>
    /// <returns>Whether this slice handles the mutation.</returns>
    public static bool Handles(OperationKind kind) => kind is OperationKind.BeginBlobUpload or OperationKind.WriteBlobPart
        or OperationKind.CompleteBlobUpload or OperationKind.AbortBlobUpload or OperationKind.DeleteBlob or OperationKind.ReclaimBlob;

    /// <summary>Rejects invalid or unenforced binary resource policies.</summary>
    /// <param name="definition">The configured resource definition.</param>
    public static void ValidatePolicy(ResourceDefinition definition) => BlobQuotaOperations.ValidatePolicy(definition);

    /// <summary>Initializes mandatory quota records in the canonical resource configuration transaction.</summary>
    /// <param name="transaction">The existing configuration transaction.</param>
    /// <param name="request">The configured resource and scope.</param>
    /// <param name="isNew">Whether the resource catalog entry is absent.</param>
    /// <param name="incarnation">The current native store authority.</param>
    public static void ConfigureResource(IAtomicTransaction transaction, ConfigureResourceRequest request, bool isNew, Guid incarnation)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);
        BlobQuotaOperations.Configure(transaction, request, isNew, incarnation);
    }

    /// <summary>Checks current persisted scope, row and creator authority before effects or replay.</summary>
    /// <param name="view">The existing gated command view.</param>
    /// <param name="principal">The current persisted principal.</param>
    /// <param name="operation">The trusted replicated command.</param>
    public void Authorize(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(operation);
        new BlobAuthority(database).Authorize(view, principal, operation);
    }

    /// <summary>Captures an immutable successful-outcome lifetime candidate before command effects.</summary>
    /// <param name="view">The current command view before effects.</param>
    /// <param name="principal">The current persisted principal.</param>
    /// <param name="operation">The trusted command.</param>
    /// <returns>The candidate stamp, or null when no selected upload exists.</returns>
    public BlobOutcomeAuthority? CaptureOutcomeAuthority(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(operation);
        return new BlobOutcomeValidation(database).Capture(view, principal, operation);
    }

    /// <summary>Checks cached successful results against the current upload lifetime.</summary>
    /// <param name="view">The current replay view.</param>
    /// <param name="operation">The trusted retry command.</param>
    /// <param name="result">The canonical stored result.</param>
    /// <param name="authority">The optional private successful-outcome stamp.</param>
    public void ValidateOutcomeAuthority(IKeyValueView view, ReplicatedOperation operation, OperationResult result, BlobOutcomeAuthority? authority)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(result);
        new BlobOutcomeValidation(database).Validate(view, operation, result, authority);
    }

    /// <summary>Stages bounded effects inside the existing command transaction and outcome commit.</summary>
    /// <param name="transaction">The borrowed canonical atomic transaction.</param>
    /// <param name="principal">The current persisted principal.</param>
    /// <param name="operation">The trusted command and evaluated time.</param>
    /// <param name="position">The canonical applied commit position.</param>
    /// <returns>The bounded typed serialized feature result.</returns>
    public OperationResult Execute(IAtomicTransaction transaction, PrincipalRecord principal, ReplicatedOperation operation, long position)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(operation);
        Authorize(transaction, principal, operation);
        var scope = BlobCommandScope.From(operation);
        var upload = new BlobUploadCommands(database);
        var publication = new BlobPublicationCommands(database);
        return operation.Kind switch
        {
            OperationKind.BeginBlobUpload => Result(transaction, operation, scope, position,
                upload.Begin(transaction, principal, BlobCommandScope.Payload<BeginBlobUploadRequest>(operation), operation.EvaluatedAt)),
            OperationKind.WriteBlobPart => Result(transaction, operation, scope, position,
                upload.Write(transaction, BlobCommandScope.Payload<WriteBlobPartRequest>(operation), operation.EvaluatedAt)),
            OperationKind.CompleteBlobUpload => Result(transaction, operation, scope, position,
                publication.Complete(transaction, BlobCommandScope.Payload<CompleteBlobUploadRequest>(operation), operation.EvaluatedAt)),
            OperationKind.AbortBlobUpload => Result(transaction, operation, scope, position,
                upload.Abort(transaction, BlobCommandScope.Payload<AbortBlobUploadRequest>(operation))),
            OperationKind.DeleteBlob => Result(transaction, operation, scope, position,
                publication.Delete(transaction, BlobCommandScope.Payload<DeleteBlobRequest>(operation), operation.EvaluatedAt)),
            OperationKind.ReclaimBlob => Result(transaction, operation, scope, position,
                new BlobReclaimCommand(database).Execute(transaction, BlobCommandScope.Payload<ReclaimBlobRequest>(operation), operation.EvaluatedAt)),
            _ => throw BlobErrors.Validation()
        };
    }

    private OperationResult Result<T>(IKeyValueView view, ReplicatedOperation operation, BlobCommandScope scope, long position, T value)
    {
        var receipt = new CommitReceipt(operation.Id, DatabaseEngine.Token(view, scope.Blob.Partition, position), [], database.Durability);
        return new(null) { NativeValue = new BlobCommitResult<T>(receipt, value) };
    }

    /// <summary>Reads authorized current metadata from one bounded storage cut.</summary>
    /// <param name="principalId">The verified principal identity.</param>
    /// <param name="request">The complete object scope.</param>
    /// <param name="cancellationToken">Cancellation for all gated work.</param>
    /// <returns>Published metadata, a tombstone, or null for an unpublished name.</returns>
    public BlobMetadata? Metadata(string principalId, BlobMetadataRequest request, CancellationToken cancellationToken = default)
        => Reads.Cut(principalId, (view, principal) => Reads.Metadata(view, principal, request), cancellationToken);

    /// <summary>Reads current creator-scoped upload progress.</summary>
    /// <param name="principalId">The verified principal identity.</param>
    /// <param name="request">The complete upload scope.</param>
    /// <param name="cancellationToken">Cancellation for all gated work.</param>
    /// <returns>Current upload progress, or null for an absent upload.</returns>
    public BlobUploadInfo? UploadInfo(string principalId, BlobUploadInfoRequest request, CancellationToken cancellationToken = default)
        => Reads.Cut(principalId, (view, principal) => Reads.UploadInfo(view, principal, request), cancellationToken);

    /// <summary>Reads an exact bounded raw range at one current positive revision.</summary>
    /// <param name="principalId">The verified principal identity.</param>
    /// <param name="request">The expected revision and bounded byte range.</param>
    /// <param name="cancellationToken">Cancellation for all gated work.</param>
    /// <returns>The independently owned requested bytes and metadata.</returns>
    public BlobReadResult Read(string principalId, BlobReadRequest request, CancellationToken cancellationToken = default)
        => Reads.Cut(principalId, (view, principal) => Reads.Range(view, principal, request), cancellationToken);

    /// <summary>Lists bounded authorized current metadata in canonical key order.</summary>
    /// <param name="principalId">The verified principal identity.</param>
    /// <param name="request">The resource scope, limit and exclusive cursor.</param>
    /// <param name="cancellationToken">Cancellation for all gated work.</param>
    /// <returns>The bounded visible page and last-visited cursor.</returns>
    public BlobListPage List(string principalId, BlobListRequest request, CancellationToken cancellationToken = default)
        => Reads.Cut(principalId, (view, principal) => Reads.List(view, principal, request), cancellationToken);

    /// <summary>Completes or resumes the offline native-restore authority normalization before serving traffic.</summary>
    public void NormalizeRestoredStore() => new BlobRestoreNormalizer(database).Normalize();
}

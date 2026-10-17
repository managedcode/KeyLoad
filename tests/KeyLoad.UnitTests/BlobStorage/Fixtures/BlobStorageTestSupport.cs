namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>Builds canonical blob requests against the real ZoneTree-backed test database.</summary>
internal static class BlobStorageTestSupport
{
    private const string ResourceName = "blobs";

    /// <summary>Configures the fixture's blob resource using the normal replicated command.</summary>
    /// <param name="database">Provides the real unit database.</param>
    public static void Configure(TestDatabase database)
    {
        var definition = new ResourceDefinition(ResourceName, ResourceKind.BlobStore, database.Partition.TransactionDomainId);
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition))
            .Get<ResourceDefinition>();
    }

    /// <summary>Creates a blob identity in the fixture's configured resource and partition.</summary>
    /// <param name="database">Provides the real unit database.</param>
    /// <param name="id">Identifies the blob key.</param>
    /// <returns>The requested resource-scoped blob key.</returns>
    public static BlobRef Blob(TestDatabase database, string id) =>
        new(database.Partition, ResourceName, id);

    /// <summary>Begins an upload using a stable command identifier.</summary>
    /// <param name="database">Provides the real unit database.</param>
    /// <param name="blob">Identifies the blob.</param>
    /// <param name="uploadId">Identifies the upload state.</param>
    /// <param name="length">Declares the complete payload length.</param>
    /// <param name="expectedRevision">Sets the head revision precondition.</param>
    /// <param name="commandId">Optionally supplies the stable command identity.</param>
    /// <returns>The replicated commit receipt and upload state.</returns>
    public static BlobCommitResult<BlobUploadInfo> Begin(TestDatabase database, BlobRef blob,
        Guid uploadId, long length, long expectedRevision, Guid? commandId = null)
    {
        var id = commandId ?? Guid.NewGuid();
        return database.Submit(OperationKind.BeginBlobUpload,
            new BeginBlobUploadRequest(id, blob, uploadId, length, expectedRevision), id: id)
            .Get<BlobCommitResult<BlobUploadInfo>>();
    }

    /// <summary>Writes a part using its canonical raw-byte hash and replicated command.</summary>
    /// <param name="database">Provides the real unit database.</param>
    /// <param name="blob">Identifies the blob.</param>
    /// <param name="uploadId">Identifies the upload state.</param>
    /// <param name="ordinal">Sets the next expected part ordinal.</param>
    /// <param name="bytes">Provides the exact raw part bytes.</param>
    /// <param name="commandId">Optionally supplies the stable command identity.</param>
    /// <returns>The replicated commit receipt and upload state.</returns>
    public static BlobCommitResult<BlobUploadInfo> Write(TestDatabase database, BlobRef blob,
        Guid uploadId, int ordinal, ReadOnlyMemory<byte> bytes, Guid? commandId = null)
    {
        var id = commandId ?? Guid.NewGuid();
        var request = new WriteBlobPartRequest(id, blob, uploadId, ordinal, bytes, BlobIntegrity.PartHash(bytes.Span));
        return database.Submit(OperationKind.WriteBlobPart, request, id: id).Get<BlobCommitResult<BlobUploadInfo>>();
    }

    /// <summary>Submits a part and exposes its stored domain rejection for negative assertions.</summary>
    /// <param name="database">Provides the real unit database.</param>
    /// <param name="blob">Identifies the blob.</param>
    /// <param name="uploadId">Identifies the upload state.</param>
    /// <param name="ordinal">Sets the requested part ordinal.</param>
    /// <param name="bytes">Provides the part bytes.</param>
    /// <returns>The underlying committed operation result.</returns>
    public static OperationResult WriteResult(TestDatabase database, BlobRef blob,
        Guid uploadId, int ordinal, ReadOnlyMemory<byte> bytes)
    {
        var id = Guid.NewGuid();
        var request = new WriteBlobPartRequest(id, blob, uploadId, ordinal, bytes, BlobIntegrity.PartHash(bytes.Span));
        return database.Submit(OperationKind.WriteBlobPart, request, id: id);
    }

    /// <summary>Publishes an upload with the caller-computed expected integrity-chain value.</summary>
    /// <param name="database">Provides the real unit database.</param>
    /// <param name="blob">Identifies the blob.</param>
    /// <param name="uploadId">Identifies the upload state.</param>
    /// <param name="integrityHash">Provides the expected final chain hash.</param>
    /// <param name="commandId">Optionally supplies the stable command identity.</param>
    /// <returns>The replicated commit receipt and published metadata.</returns>
    public static BlobCommitResult<BlobMetadata> Complete(TestDatabase database, BlobRef blob,
        Guid uploadId, string integrityHash, Guid? commandId = null)
    {
        var id = commandId ?? Guid.NewGuid();
        return database.Submit(OperationKind.CompleteBlobUpload,
            new CompleteBlobUploadRequest(id, blob, uploadId, integrityHash), id: id)
            .Get<BlobCommitResult<BlobMetadata>>();
    }

    /// <summary>Submits completion and exposes a persisted safe rejection for negative assertions.</summary>
    /// <param name="database">Provides the real unit database.</param>
    /// <param name="blob">Identifies the blob.</param>
    /// <param name="uploadId">Identifies the upload state.</param>
    /// <param name="integrityHash">Provides the caller's expected final chain hash.</param>
    /// <returns>The underlying committed operation result.</returns>
    public static OperationResult CompleteResult(TestDatabase database, BlobRef blob,
        Guid uploadId, string integrityHash)
    {
        var id = Guid.NewGuid();
        return database.Submit(OperationKind.CompleteBlobUpload,
            new CompleteBlobUploadRequest(id, blob, uploadId, integrityHash), id: id);
    }
}

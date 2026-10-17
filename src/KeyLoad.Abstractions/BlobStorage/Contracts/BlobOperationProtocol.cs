namespace KeyLoad;

/// <summary>Canonical blob routes shared by HTTP, SQL, SDK and official MCP adapters.</summary>
public static class BlobOperationProtocol
{
    /// <summary>Begins an upload.</summary>
    public const string BeginUpload = "/v1/blobs/uploads/begin";
    /// <summary>Writes one bounded part.</summary>
    public const string WritePart = "/v1/blobs/uploads/parts";
    /// <summary>Publishes an upload.</summary>
    public const string CompleteUpload = "/v1/blobs/uploads/complete";
    /// <summary>Aborts an upload.</summary>
    public const string AbortUpload = "/v1/blobs/uploads/abort";
    /// <summary>Publishes a deletion tombstone.</summary>
    public const string Delete = "/v1/blobs/delete";
    /// <summary>Reclaims retired version parts.</summary>
    public const string Reclaim = "/v1/blobs/reclaim";
    /// <summary>Reads current metadata.</summary>
    public const string Metadata = "/v1/blobs/metadata";
    /// <summary>Reads upload progress.</summary>
    public const string UploadInfo = "/v1/blobs/uploads/info";
    /// <summary>Reads a bounded raw range.</summary>
    public const string ReadRange = "/v1/blobs/range";
    /// <summary>Lists live metadata.</summary>
    public const string List = "/v1/blobs/list";
}

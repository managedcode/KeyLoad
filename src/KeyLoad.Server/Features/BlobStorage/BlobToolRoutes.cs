namespace KeyLoad.Server;

/// <summary>Shared canonical HTTP routes for native MCP and direct BlobStorage adapters.</summary>
internal static class BlobToolRoutes
{
    internal const string BeginUpload = "/v1/blobs/uploads/begin";
    internal const string WritePart = "/v1/blobs/uploads/parts";
    internal const string CompleteUpload = "/v1/blobs/uploads/complete";
    internal const string AbortUpload = "/v1/blobs/uploads/abort";
    internal const string Delete = "/v1/blobs/delete";
    internal const string Reclaim = "/v1/blobs/reclaim";
    internal const string Metadata = "/v1/blobs/metadata";
    internal const string UploadInfo = "/v1/blobs/uploads/info";
    internal const string ReadRange = "/v1/blobs/range";
    internal const string List = "/v1/blobs/list";
}

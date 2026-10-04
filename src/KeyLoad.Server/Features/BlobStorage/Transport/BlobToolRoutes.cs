namespace KeyLoad.Server;

/// <summary>Shared canonical HTTP routes for native MCP and direct BlobStorage adapters.</summary>
internal static class BlobToolRoutes
{
    internal const string BeginUpload = BlobOperationProtocol.BeginUpload;
    internal const string WritePart = BlobOperationProtocol.WritePart;
    internal const string CompleteUpload = BlobOperationProtocol.CompleteUpload;
    internal const string AbortUpload = BlobOperationProtocol.AbortUpload;
    internal const string Delete = BlobOperationProtocol.Delete;
    internal const string Reclaim = BlobOperationProtocol.Reclaim;
    internal const string Metadata = BlobOperationProtocol.Metadata;
    internal const string UploadInfo = BlobOperationProtocol.UploadInfo;
    internal const string ReadRange = BlobOperationProtocol.ReadRange;
    internal const string List = BlobOperationProtocol.List;
}

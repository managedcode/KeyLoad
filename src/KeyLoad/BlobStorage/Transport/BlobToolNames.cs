namespace KeyLoad.Server;

/// <summary>The accepted native MCP identities for the public BlobStorage slice.</summary>
internal static class BlobToolNames
{
    internal const string BeginUpload = "keyload_blobs_begin_upload";
    internal const string WritePart = "keyload_blobs_write_part";
    internal const string CompleteUpload = "keyload_blobs_complete_upload";
    internal const string AbortUpload = "keyload_blobs_abort_upload";
    internal const string Delete = "keyload_blobs_delete";
    internal const string Reclaim = "keyload_blobs_reclaim";
    internal const string Metadata = "keyload_blobs_metadata";
    internal const string UploadInfo = "keyload_blobs_upload_info";
    internal const string ReadRange = "keyload_blobs_read_range";
    internal const string List = "keyload_blobs_list";
}

namespace KeyLoad.Server.Features.BlobStorage;

internal enum NativeBlobWireIdentity
{
    BeginUpload,
    WritePart,
    CompleteUpload,
    AbortUpload,
    Delete,
    Reclaim,
    Metadata,
    UploadInfo,
    Range,
    List
}

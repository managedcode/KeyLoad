using System.Collections.Immutable;
using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Typed native MCP bindings that share the canonical DTOs, routes and operation dispatch.</summary>
internal static class BlobMcpCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries
    {
        get;
    } =
    [
        McpOperationFactory.Command<BeginBlobUploadRequest, BlobCommitResult<BlobUploadInfo>>(BlobToolNames.BeginUpload, BlobToolRoutes.BeginUpload, OperationKind.BeginBlobUpload, static request => request.CommandId),
        McpOperationFactory.Command<WriteBlobPartRequest, BlobCommitResult<BlobUploadInfo>>(BlobToolNames.WritePart, BlobToolRoutes.WritePart, OperationKind.WriteBlobPart, static request => request.CommandId),
        McpOperationFactory.Command<CompleteBlobUploadRequest, BlobCommitResult<BlobMetadata>>(BlobToolNames.CompleteUpload, BlobToolRoutes.CompleteUpload, OperationKind.CompleteBlobUpload, static request => request.CommandId),
        McpOperationFactory.Command<AbortBlobUploadRequest, BlobCommitResult<BlobUploadInfo>>(BlobToolNames.AbortUpload, BlobToolRoutes.AbortUpload, OperationKind.AbortBlobUpload, static request => request.CommandId),
        McpOperationFactory.Command<DeleteBlobRequest, BlobCommitResult<BlobMetadata>>(BlobToolNames.Delete, BlobToolRoutes.Delete, OperationKind.DeleteBlob, static request => request.CommandId),
        McpOperationFactory.Command<ReclaimBlobRequest, BlobCommitResult<BlobReclaimResult>>(BlobToolNames.Reclaim, BlobToolRoutes.Reclaim, OperationKind.ReclaimBlob, static request => request.CommandId),
        McpOperationFactory.Read<BlobMetadataRequest, BlobMetadataReadResult>(BlobToolNames.Metadata, BlobToolRoutes.Metadata, GrainReadKind.BlobMetadata),
        McpOperationFactory.Read<BlobUploadInfoRequest, BlobUploadInfoReadResult>(BlobToolNames.UploadInfo, BlobToolRoutes.UploadInfo, GrainReadKind.BlobUploadInfo),
        McpOperationFactory.Read<BlobReadRequest, BlobReadResult>(BlobToolNames.ReadRange, BlobToolRoutes.ReadRange, GrainReadKind.BlobRange),
        McpOperationFactory.Read<BlobListRequest, BlobListPage>(BlobToolNames.List, BlobToolRoutes.List, GrainReadKind.BlobList)
    ];
}

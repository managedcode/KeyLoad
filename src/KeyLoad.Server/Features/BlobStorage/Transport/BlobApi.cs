using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Routes ten typed BlobStorage operations through the shared fresh-request grain gateway.</summary>
internal static class BlobApi
{
    internal static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost(BlobToolRoutes.BeginUpload, (BeginBlobUploadRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.BeginBlobUpload, request.CommandId, request));
        app.MapPost(BlobToolRoutes.WritePart, (WriteBlobPartRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.WriteBlobPart, request.CommandId, request));
        app.MapPost(BlobToolRoutes.CompleteUpload, (CompleteBlobUploadRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.CompleteBlobUpload, request.CommandId, request));
        app.MapPost(BlobToolRoutes.AbortUpload, (AbortBlobUploadRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.AbortBlobUpload, request.CommandId, request));
        app.MapPost(BlobToolRoutes.Delete, (DeleteBlobRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.DeleteBlob, request.CommandId, request));
        app.MapPost(BlobToolRoutes.Reclaim, (ReclaimBlobRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ReclaimBlob, request.CommandId, request));
        app.MapPost(BlobToolRoutes.Metadata, (BlobMetadataRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.BlobMetadata, request));
        app.MapPost(BlobToolRoutes.UploadInfo, (BlobUploadInfoRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.BlobUploadInfo, request));
        app.MapPost(BlobToolRoutes.ReadRange, (BlobReadRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.BlobRange, request));
        app.MapPost(BlobToolRoutes.List, (BlobListRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.BlobList, request));
    }
}

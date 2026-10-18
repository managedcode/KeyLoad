using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.Features.BlobStorage;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobCommandScope)]
internal sealed record BlobCommandScope(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobCommandScopeFields.CommandId)] Guid CommandId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobCommandScopeFields.Blob)] BlobRef Blob,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobCommandScopeFields.UploadId)] Guid? UploadId)
{
    internal static T Payload<T>(Command<DatabaseCommand> operation) => DatabaseEngine.Payload<T>(operation);
    internal static Capability RequiredCapability(OperationKind kind) => kind switch
    {
        OperationKind.DeleteBlob => Capability.BlobDelete,
        OperationKind.ReclaimBlob => Capability.BlobManage,
        _ => Capability.BlobWrite
    };
    internal static BlobCommandScope From(Command<DatabaseCommand> operation) => operation.Value!.Kind switch
    {
        OperationKind.BeginBlobUpload => Begin(Payload<BeginBlobUploadRequest>(operation)),
        OperationKind.WriteBlobPart => Write(Payload<WriteBlobPartRequest>(operation)),
        OperationKind.CompleteBlobUpload => Complete(Payload<CompleteBlobUploadRequest>(operation)),
        OperationKind.AbortBlobUpload => Abort(Payload<AbortBlobUploadRequest>(operation)),
        OperationKind.DeleteBlob => Delete(Payload<DeleteBlobRequest>(operation)),
        OperationKind.ReclaimBlob => Reclaim(Payload<ReclaimBlobRequest>(operation)),
        _ => throw BlobErrors.Validation()
    };
    private static BlobCommandScope Begin(BeginBlobUploadRequest request) => new(request.CommandId, request.Blob, request.UploadId);
    private static BlobCommandScope Write(WriteBlobPartRequest request) => new(request.CommandId, request.Blob, request.UploadId);
    private static BlobCommandScope Complete(CompleteBlobUploadRequest request) => new(request.CommandId, request.Blob, request.UploadId);
    private static BlobCommandScope Abort(AbortBlobUploadRequest request) => new(request.CommandId, request.Blob, request.UploadId);
    private static BlobCommandScope Delete(DeleteBlobRequest request) => new(request.CommandId, request.Blob, null);
    private static BlobCommandScope Reclaim(ReclaimBlobRequest request) => new(request.CommandId, request.Blob, request.UploadId);
}

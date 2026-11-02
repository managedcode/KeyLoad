using KeyLoad.Core.BlobStorage;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad.Core;
/// <summary>Executes FileDispatch operations against the caller's database context.</summary>
public static class FileDispatch
{
    internal static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ ExecuteFileMetadata(IAtomicTransaction transaction, PrincipalRecord principal, FileMetadataMutation request, DateTime evaluatedAt)
    {
        if (request.Action == FileMetadataAction.Reserve)
        {
            return OperationProtocol.Result(BlobStorageCommandsFileMetadata.ReserveFile(transaction, principal, request)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */;
        }
        if (request.Action == FileMetadataAction.Delete)
        {
            return OperationProtocol.Result(FilePublication.DeleteFile(transaction, request, evaluatedAt)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */;
        }
        var (policy, key, upload) = BlobStorageCommandsFileMetadata.ReadFileUpload(transaction, principal, request);
        return request.Action switch
        {
            FileMetadataAction.Stage => OperationProtocol.Result(BlobStorageCommandsFileMetadata.StageFile(transaction, key, upload, request)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
            FileMetadataAction.Publish => OperationProtocol.Result(FilePublication.PublishFile(transaction, key, upload, request, policy, evaluatedAt)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
            FileMetadataAction.Abort => OperationProtocol.Result(FilePublication.AbortFile(transaction, key, upload, request)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
            FileMetadataAction.Reclaim => OperationProtocol.Result(FilePublication.ReclaimFile(transaction, key, upload)) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */,
            _ => throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid)
        };
    }
}

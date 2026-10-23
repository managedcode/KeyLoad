using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad.Core;
/// <summary>Executes FileDispatch operations against the caller's database context.</summary>
public static class FileDispatch
{
    internal static Result<object> ExecuteFileMetadata(IAtomicTransaction transaction, PrincipalRecord principal, FileMetadataMutation request, DateTime evaluatedAt) => OperationProtocol.Result(request.Action switch
    {
        FileMetadataAction.Reserve => BlobStorageCommandsFileMetadata.ReserveFile(transaction, principal, request),
        FileMetadataAction.Delete => FilePublication.DeleteFile(transaction, request, evaluatedAt),
        _ => BlobStorageCommandsFileMetadata.ChangeFileUpload(transaction, principal, request, evaluatedAt)
    });
}

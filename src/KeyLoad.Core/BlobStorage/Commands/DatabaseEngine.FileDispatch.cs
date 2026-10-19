using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static Result<object> ExecuteFileMetadata(IAtomicTransaction transaction, PrincipalRecord principal,
        FileMetadataMutation request, DateTime evaluatedAt) => Result(request.Action switch
        {
            FileMetadataAction.Reserve => ReserveFile(transaction, principal, request),
            FileMetadataAction.Delete => DeleteFile(transaction, request, evaluatedAt),
            _ => ChangeFileUpload(transaction, principal, request, evaluatedAt)
        });
}

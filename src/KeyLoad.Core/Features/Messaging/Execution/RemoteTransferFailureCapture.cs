using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteTransferAcceptFailureAuthority? CaptureRemoteTransferFailureAuthority(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        if (Limits.MaxQueueTransferAcceptAttempts is null || operation.Kind != OperationKind.Batch)
        { return null; }
        var batch = Payload<CommandRequest>(operation);
        if (batch.Mutations.Length != RemoteTransferAttemptProtocol.SingleMutation
            || batch.Mutations[RemoteTransferAttemptProtocol.FirstIndex] is not AcceptQueueTransfer accept)
        { return null; }
        var claims = AuthorizeAcceptQueueTransfer(view, principal, batch.Partition, accept);
        var existing = view.GetRecord<RemoteTransferTargetReceiptRecord>(
            RemoteTransferStorage.TargetReceiptKey(claims.Source, claims.TransferId, claims.Destination));
        if (existing is not null)
        { return null; }
        return new(claims.Source, claims.Destination, claims.TransferId, principal.Id, claims.Fingerprint,
            RemoteTransferCoordinationIdentity.IntentDigest(accept.IntentToken), operation.Id, CommandFingerprint(operation),
            Store.Identity.Incarnation, CaptureRemoteTransferDependency(view, principal, claims.Destination));
    }

    private static bool HasRemoteTransferCapacityFailure(OperationResult result)
        => result.Error == ErrorCode.ResourceExhausted && (result.SafeDetail == StoredQuotaExhausted
            || RemoteTransferStorage.IsCapacityFailure(result));

    private static RemoteTransferAcceptFailureKind RemoteTransferFailureKind(OperationResult result)
        => result.SafeDetail == StoredQuotaExhausted ? RemoteTransferAcceptFailureKind.QueueStorage
            : RemoteTransferAcceptFailureKind.TransferRetention;
}

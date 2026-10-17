using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteQueueTransferPeerResult ObserveRemoteTransferPeer(AdmittedRemoteTransferCall admitted,
        ReadExecutionBudget work)
        => ObserveRemoteTransferPeerCore(admitted, work, bindResourceLifetime: false);

    internal RemoteQueueTransferPeerResult ObserveRemoteTransferPeerBound(AdmittedRemoteTransferCall admitted,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        return ObserveRemoteTransferPeerCore(admitted, work, bindResourceLifetime: true);
    }

    private RemoteQueueTransferPeerResult ObserveRemoteTransferPeerCore(AdmittedRemoteTransferCall admitted,
        ReadExecutionBudget work, bool bindResourceLifetime)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        work.Check();
        var proof = admitted.Proof;
        var call = proof.Call;
        if (call.ExpiresAt <= Clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid);
        }
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, proof.TechnicalPrincipalId, Clock.GetUtcNow());
            RequireRemoteTransferPeerOwner().Require(proof);
            RequireRemoteTransferPeerAtView(charged, principal, proof, bindResourceLifetime);
            if (call.Stage == RemoteQueueTransferPeerStage.Receipt)
            {
                return new RemoteQueueTransferPeerResult(call.Stage, ReadRemoteTransferPeerReceipt(charged, principal, call), null);
            }
            return new RemoteQueueTransferPeerResult(call.Stage, null, ReadRemoteTransferPeerOutcome(charged, principal, call));
        });
        var bytes = NativeSerialization.Measure(result);
        RequireNativeBudget(bytes);
        if (bytes > call.MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteTransferPeerProtocol.Unavailable);
        }
        work.ChargeBytes(bytes);
        work.Check();
        if (bindResourceLifetime)
        {
            work.CheckResult(result);
        }
        return result;
    }

    private QueueTransferReceiptInspection? ReadRemoteTransferPeerReceipt(IKeyValueView view,
        PrincipalRecord principal, RemoteQueueTransferPeerCall call)
    {
        var intent = call.IntentClaims;
        var receipt = InspectQueueTransferReceipt(view, principal.Id, intent.Destination, intent.Source, intent.TransferId);
        if (receipt is null)
        {
            return null;
        }
        var record = view.GetRecord<RemoteTransferTargetReceiptRecord>(
            RemoteTransferStorage.TargetReceiptKey(intent.Source, intent.TransferId, intent.Destination))
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        RequireExistingTargetReceipt(view, record, intent);
        RequireRemoteTransferOrigin(record.RemoteOrigin, new(call.SourceOwner, intent.PrincipalId,
            RemoteTransferCoordinationIdentity.IntentDigest(call.IntentToken)));
        return receipt;
    }
}

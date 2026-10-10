using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteQueueTransferPeerResult ObserveRemoteTransferPeer(AdmittedRemoteTransferCall admitted,
        ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        work.Check();
        var proof = admitted.Proof;
        var call = proof.Call;
        if (call.ExpiresAt <= Clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, proof.TechnicalPrincipalId, Clock.GetUtcNow());
            RequireRemoteTransferPeerOwner().Require(proof);
            RequireRemoteTransferPeerAtView(charged, principal, proof);
            if (call.Stage == RemoteQueueTransferPeerStage.Receipt)
            { return new RemoteQueueTransferPeerResult(call.Stage, ReadRemoteTransferPeerReceipt(charged, principal, call), null); }
            return new RemoteQueueTransferPeerResult(call.Stage, null, ReadRemoteTransferPeerOutcome(charged, principal, call));
        });
        var bytes = NativeSerialization.Measure(result);
        RequireNativeBudget(bytes);
        if (bytes > call.MaximumReplyBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteTransferPeerProtocol.Unavailable); }
        work.ChargeBytes(bytes);
        work.Check();
        return result;
    }

    private QueueTransferReceiptInspection? ReadRemoteTransferPeerReceipt(IKeyValueView view,
        PrincipalRecord principal, RemoteQueueTransferPeerCall call)
    {
        var intent = call.IntentClaims;
        var receipt = InspectQueueTransferReceipt(view, principal.Id, intent.Destination, intent.Source, intent.TransferId);
        if (receipt is null)
        { return null; }
        var record = view.GetRecord<RemoteTransferTargetReceiptRecord>(
            RemoteTransferStorage.TargetReceiptKey(intent.Source, intent.TransferId, intent.Destination))
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        RequireExistingTargetReceipt(view, record, intent);
        RequireRemoteTransferOrigin(record.RemoteOrigin, new(call.SourceOwner, intent.PrincipalId,
            RemoteTransferCoordinationIdentity.IntentDigest(call.IntentToken)));
        return receipt;
    }
}

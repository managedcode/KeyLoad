using System.Collections.Immutable;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateRemoteTransferNativeOperation(AdmittedRemoteTransferCall admitted,
        ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        work.Check();
        var proof = admitted.Proof;
        var call = proof.Call;
        if (call.Stage != RemoteQueueTransferPeerStage.Accept || call.ExpiresAt <= Clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        var placement = Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, proof.TechnicalPrincipalId, proof.OriginalEvaluatedAt);
            RequireRemoteTransferPeerOwner().Require(proof);
            RequireRemoteTransferPeerAtView(charged, principal, proof);
            return ReadPlacementWitness(charged, call.IntentClaims.Destination.Partition);
        });
        var request = new CommandRequest(call.OriginalCommandId, call.IntentClaims.Destination.Partition,
            ImmutableArray.Create<Mutation>(new AcceptQueueTransfer(call.IntentClaims.Destination, call.IntentToken)),
            placement.PlacementEpoch);
        var value = NativeSerialization.Serialize(request);
        RequireNativeBudget(value.Length);
        work.ChargeBytes(value.Length);
        var encodedProof = NativeSerialization.Serialize(proof);
        RequireNativeBudget(encodedProof.Length);
        work.ChargeBytes(encodedProof.Length);
        var operation = new ReplicatedOperation(call.OriginalCommandId, OperationKind.Batch,
            proof.TechnicalPrincipalId, proof.OriginalEvaluatedAt, Identity<CommandRequest>(value));
        var issued = IssueNativeOperation(operation, new NativeCommandPayload(value) { TransferProof = encodedProof });
        work.Check();
        return issued;
    }
}

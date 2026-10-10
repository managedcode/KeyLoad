using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteTransferOutcomeAuthority? CaptureRemoteTransferOutcomeAuthority(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var proof = ReadRemoteTransferNativeProof(operation);
        if (proof is null)
        { return null; }
        RequireRemoteTransferPeerAtView(view, principal, proof);
        return RemoteTransferAuthorityFromProof(proof);
    }

    private static RemoteTransferOutcomeAuthority RemoteTransferAuthorityFromProof(RemoteTransferNativeProof proof)
        => new(proof.Call.LogicalPrincipalId, proof.Call.LogicalPolicyEpoch, proof.Call.FieldHeaderDigest,
            proof.Call.SourceOwner, proof.Call.DestinationOwner, proof.TechnicalPrincipalId,
            proof.TechnicalPolicyEpoch, Convert.ToHexString(SHA256.HashData(NativeSerialization.Serialize(proof))));

    private bool ValidateRemoteTransferCachedOutcome(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation, StoredOutcome outcome)
    {
        var proof = ReadRemoteTransferNativeProof(operation);
        if (proof is null)
        {
            if (outcome.RemoteTransferAuthority is not null)
            { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid); }
            return false;
        }
        RequireRemoteTransferPeerAtView(view, principal, proof);
        if (outcome.Result.Error is not null)
        {
            if (outcome.RemoteTransferAuthority is not null)
            { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid); }
            return true;
        }
        RequireRemoteTransferObservedStamp(outcome, proof.Call, principal);
        _ = ReadRemoteTransferPeerReceipt(view, principal, proof.Call)
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        return true;
    }
}

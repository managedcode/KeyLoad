using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteTransferCoordinationReadResult ReadRemoteTransferTargetFailure(IKeyValueView view,
        PrincipalRecord principal, RemoteTransferCoordinationReadRequest request)
    {
        var original = Verify<RemoteTransferIntentClaims>(request.IntentToken, Limits.MaxBatchBytes);
        ValidateIntentClaims(original, request.Destination, principal.Id);
        if (original.Source != request.Source || original.TransferId != request.TransferId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferAttemptProtocol.Invalid); }
        var receipt = InspectQueueTransferReceipt(view, principal.Id, request.Destination, request.Source, request.TransferId);
        if (receipt is not null)
        { return new(null, null, receipt); }
        var selection = CommandOutcomeKeyResolver.Select(view, principal.Id, request.AcceptCommandId,
            new(CommandOutcomeScopeKind.Partition, request.Destination.Partition));
        var outcome = selection.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        var authority = RequireRemoteTransferFailedAuthority(outcome, original, request);
        var bytes = view.ReadOwnedValue(selection.Key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        var cut = Token(view, request.Destination.Partition, Store.Position);
        var claims = new RemoteTransferAcceptFailureClaims(RemoteTransferAttemptProtocol.FailurePurpose, authority,
            Convert.ToHexString(SHA256.HashData(bytes)), CaptureRemoteTransferDependency(view, principal, request.Destination),
            cut, cut.Position, RemoteTransferFailureKind(outcome.Result));
        if (!RemoteTransferCapacityRepair.Improved(claims))
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale); }
        var signed = Sign(claims);
        RequireTokenBound(signed);
        return new(null, signed, null);
    }

    private RemoteTransferAcceptFailureAuthority RequireRemoteTransferFailedAuthority(StoredOutcome outcome,
        RemoteTransferIntentClaims intent, RemoteTransferCoordinationReadRequest request)
    {
        var stamp = outcome.RemoteTransferFailureAuthority;
        if (stamp is null || outcome.Result is null || !RemoteTransferDependencyShape.Valid(stamp.Dependency)
            || !HasRemoteTransferCapacityFailure(outcome.Result) || (outcome.Result.Json is not null || outcome.Result.NativeValue is not null))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferAttemptProtocol.Unavailable); }
        var intentDigest = RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken);
        if (outcome.Incarnation != Store.Identity.Incarnation || stamp.TargetIncarnation != outcome.Incarnation
            || stamp.Source != intent.Source || stamp.Destination != intent.Destination || stamp.TransferId != intent.TransferId
            || stamp.PrincipalId != intent.PrincipalId || stamp.MessageFingerprint != intent.Fingerprint
            || stamp.IntentDigest != intentDigest || stamp.AcceptCommandId != request.AcceptCommandId
            || stamp.CommandFingerprint != outcome.Fingerprint || stamp.Dependency.PrincipalPolicyEpoch != outcome.PolicyEpoch
            || RemoteTransferAttemptIdentity.AcceptId(intent.Source, intent.Destination, intent.TransferId,
                intent.PrincipalId, intent.Fingerprint, intentDigest, intent.Incarnation, request.ExpectedGeneration) != request.AcceptCommandId)
        { throw Errors.Fail(ErrorCode.Conflict, RemoteTransferAttemptProtocol.Invalid); }
        return stamp;
    }
}

using System.Security.Cryptography;
using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferTargetFailureRead operations against the caller's database context.</summary>
public static class RemoteTransferTargetFailureRead
{
    internal static RemoteTransferCoordinationReadResult ReadRemoteTransferTargetFailure(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, RemoteTransferCoordinationReadRequest request)
    {
        var original = engine.Verify<RemoteTransferIntentClaims>(request.IntentToken, engine.Limits.MaxBatchBytes);
        engine.ValidateIntentClaims(original, request.Destination, principal.Id);
        if (original.Source != request.Source || original.TransferId != request.TransferId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferAttemptProtocol.Invalid);
        }

        var receipt = engine.InspectQueueTransferReceipt(view, principal.Id, request.Destination, request.Source, request.TransferId);
        if (receipt is not null)
        {
            return new(null, null, receipt);
        }

        var selection = CommandOutcomeKeyResolver.Select(view, principal.Id, request.AcceptCommandId, new(CommandOutcomeScopeKind.Partition, request.Destination.Partition));
        var outcome = selection.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        var authority = engine.RequireRemoteTransferFailedAuthority(outcome, original, request);
        var bytes = view.ReadOwnedValue(selection.Key) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        var cut = engine.Token(view, request.Destination.Partition, engine.Store.Position);
        var claims = new RemoteTransferAcceptFailureClaims(RemoteTransferAttemptProtocol.FailurePurpose, authority, Convert.ToHexString(SHA256.HashData(bytes)), engine.CaptureRemoteTransferDependency(view, principal, request.Destination), cut, cut.Position, RemoteTransferFailureCapture.RemoteTransferFailureKind(outcome.Result));
        if (!RemoteTransferCapacityRepair.Improved(claims))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale);
        }

        var signed = engine.Sign(claims);
        engine.RequireTokenBound(signed);
        return new(null, signed, null);
    }

    internal static RemoteTransferAcceptFailureAuthority RequireRemoteTransferFailedAuthority(this DatabaseEngine engine, StoredOutcome outcome, RemoteTransferIntentClaims intent, RemoteTransferCoordinationReadRequest request)
    {
        var stamp = outcome.RemoteTransferFailureAuthority;
        if (stamp is null || !RemoteTransferDependencyShape.Valid(stamp.Dependency) || !RemoteTransferFailureCapture.HasRemoteTransferCapacityFailure(outcome.Result) || (outcome.Result.Value is not null))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferAttemptProtocol.Unavailable);
        }

        var intentDigest = RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken);
        if (outcome.Incarnation != engine.Store.Identity.Incarnation || stamp.TargetIncarnation != outcome.Incarnation || stamp.Source != intent.Source || stamp.Destination != intent.Destination || stamp.TransferId != intent.TransferId || stamp.PrincipalId != intent.PrincipalId || stamp.MessageFingerprint != intent.Fingerprint || stamp.IntentDigest != intentDigest || stamp.AcceptCommandId != request.AcceptCommandId || stamp.CommandFingerprint != outcome.Fingerprint || stamp.Dependency.PrincipalPolicyEpoch != outcome.PolicyEpoch || RemoteTransferAttemptIdentity.AcceptId(intent.Source, intent.Destination, intent.TransferId, intent.PrincipalId, intent.Fingerprint, intentDigest, intent.Incarnation, request.ExpectedGeneration) != request.AcceptCommandId)
        {
            throw Errors.Fail(ErrorCode.Conflict, RemoteTransferAttemptProtocol.Invalid);
        }

        return stamp;
    }
}

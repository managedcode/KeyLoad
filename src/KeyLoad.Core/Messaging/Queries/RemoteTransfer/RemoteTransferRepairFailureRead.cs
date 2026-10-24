using System.Security.Cryptography;
using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferRepairFailureRead operations against the caller's database context.</summary>
public static class RemoteTransferRepairFailureRead
{
    internal static RemoteTransferCoordinationReadResult ReadRemoteTransferRepairFailure(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, RemoteTransferCoordinationReadRequest request)
    {
        if (engine.Limits.MaxQueueTransferRepairAttempts is null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferRepairProtocol.Unavailable);
        }

        var intent = engine.Verify<RemoteTransferIntentClaims>(request.IntentToken, engine.Limits.MaxBatchBytes);
        engine.ValidateIntentClaims(intent, request.Destination, principal.Id);
        if (intent.Source != request.Source || intent.TransferId != request.TransferId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferRepairProtocol.Invalid);
        }

        if (request.RepairStage == QueueTransferRepairStage.Accept)
        {
            var receipt = engine.InspectQueueTransferReceipt(view, principal.Id, request.Destination, request.Source, request.TransferId);
            if (receipt is not null)
            {
                return new(null, null, receipt);
            }
        }

        var original = engine.RequireRemoteTransferRepairOriginal(view, principal, request, intent);
        var partition = request.RepairStage == QueueTransferRepairStage.Accept ? request.Destination.Partition : request.Source.Partition;
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, original.CommandId, new(CommandOutcomeScopeKind.Partition, partition));
        var outcome = selected.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
        if (outcome.Incarnation != engine.Store.Identity.Incarnation || outcome.Fingerprint != OperationProtocol.CommandFingerprint(original))
        {
            throw Errors.Fail(ErrorCode.Conflict, RemoteTransferRepairProtocol.Invalid);
        }

        if (Errors.Code(outcome.Result.Problem) != ErrorCode.PermissionDenied || outcome.Result.Value is not null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferRepairProtocol.Unavailable);
        }

        if (outcome.PolicyEpoch >= principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale);
        }

        var bytes = view.ReadOwnedValue(selected.Key) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
        var resource = engine.Resource(view, partition, request.RepairStage == QueueTransferRepairStage.Accept ? request.Destination.Queue : request.Source.Queue, ResourceKind.WorkQueue);
        var cut = engine.Token(view, partition, engine.Store.Position);
        var claims = new RemoteTransferRepairClaims(RemoteTransferRepairProtocol.Purpose, intent.Source, intent.Destination, intent.TransferId, principal.Id, intent.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken), request.RepairStage!.Value, request.ExpectedGeneration, request.PolicyGeneration, request.CompleteGeneration, original.CommandId, outcome.Fingerprint, Convert.ToHexString(SHA256.HashData(bytes)), outcome.PolicyEpoch, principal.PolicyEpoch, JsonData.Fingerprint(new { resource.FieldPolicies, resource.HeaderPolicies }), cut, cut.Position, request.ReceiptToken is null ? null : RemoteTransferCoordinationIdentity.IntentDigest(request.ReceiptToken));
        var signed = engine.Sign(claims);
        engine.RequireTokenBound(signed);
        return new(null, null, null, signed);
    }
}

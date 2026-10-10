using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteTransferCoordinationReadResult ReadRemoteTransferRepairFailure(IKeyValueView view,
        PrincipalRecord principal, RemoteTransferCoordinationReadRequest request)
    {
        if (Limits.MaxQueueTransferRepairAttempts is null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferRepairProtocol.Unavailable); }
        var intent = Verify<RemoteTransferIntentClaims>(request.IntentToken, Limits.MaxBatchBytes);
        ValidateIntentClaims(intent, request.Destination, principal.Id);
        if (intent.Source != request.Source || intent.TransferId != request.TransferId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferRepairProtocol.Invalid); }
        if (request.RepairStage == QueueTransferRepairStage.Accept)
        {
            var receipt = InspectQueueTransferReceipt(view, principal.Id, request.Destination, request.Source, request.TransferId);
            if (receipt is not null)
            { return new(null, null, receipt); }
        }
        var original = RequireRemoteTransferRepairOriginal(view, principal, request, intent);
        var partition = request.RepairStage == QueueTransferRepairStage.Accept ? request.Destination.Partition : request.Source.Partition;
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, original.Id,
            new(CommandOutcomeScopeKind.Partition, partition));
        var outcome = selected.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
        if (outcome.Incarnation != Store.Identity.Incarnation || outcome.Fingerprint != CommandFingerprint(original))
        { throw Errors.Fail(ErrorCode.Conflict, RemoteTransferRepairProtocol.Invalid); }
        if (outcome.Result is null || outcome.Result.Error != ErrorCode.PermissionDenied || outcome.Result.Json is not null
            || outcome.Result.NativeValue is not null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferRepairProtocol.Unavailable); }
        if (outcome.PolicyEpoch >= principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale); }
        var bytes = view.ReadOwnedValue(selected.Key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
        var resource = Resource(view, partition, request.RepairStage == QueueTransferRepairStage.Accept
            ? request.Destination.Queue : request.Source.Queue, ResourceKind.WorkQueue);
        var cut = Token(view, partition, Store.Position);
        var claims = new RemoteTransferRepairClaims(RemoteTransferRepairProtocol.Purpose, intent.Source, intent.Destination,
            intent.TransferId, principal.Id, intent.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken),
            request.RepairStage!.Value, request.ExpectedGeneration, request.PolicyGeneration, request.CompleteGeneration,
            original.Id, outcome.Fingerprint, Convert.ToHexString(SHA256.HashData(bytes)), outcome.PolicyEpoch, principal.PolicyEpoch,
            JsonData.Fingerprint(new { resource.FieldPolicies, resource.HeaderPolicies }), cut,
            cut.Position, request.ReceiptToken is null ? null : RemoteTransferCoordinationIdentity.IntentDigest(request.ReceiptToken));
        var signed = Sign(claims);
        RequireTokenBound(signed);
        return new(null, null, null, signed);
    }
}

using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteTransferCoordinationReadResult ReadRemoteTransferCoordination(string subject,
        RemoteTransferCoordinationReadRequest request, CancellationToken token)
    {
        RemoteTransferAttemptReadShape.Require(request);
        using var budget = RemoteTransferPendingBudget.Coordination(this, token);
        var result = Store.Read(view => ReadRemoteTransferCoordination(budget.View(view), subject, request));
        budget.Result(result);
        return result;
    }

    private RemoteTransferCoordinationReadResult ReadRemoteTransferCoordination(IKeyValueView view, string subject,
        RemoteTransferCoordinationReadRequest request)
    {
        ValidateTransferPair(request.Source, request.Destination);
        var principal = Principal(view, subject, Clock.GetUtcNow());
        var sourceRead = request.Purpose == RemoteTransferAttemptProtocol.SourceReadPurpose
            || request.Purpose == RemoteTransferRepairProtocol.ReadPurpose && request.RepairStage == QueueTransferRepairStage.Complete;
        var lane = sourceRead ? request.Source : request.Destination;
        if (principal.TenantId != lane.Partition.TenantId)
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteTransferAttemptProtocol.Invalid); }
        RequireTransferAdministrator(principal);
        var resource = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        RequireTransferInspector(principal, lane, resource);
        RequireTransferPublisher(principal, lane, resource);
        if (request.Purpose == RemoteTransferRepairProtocol.ReadPurpose)
        { return ReadRemoteTransferRepairFailure(view, principal, request); }
        return request.Purpose == RemoteTransferAttemptProtocol.SourceReadPurpose
            ? ReadRemoteTransferSourceAttempt(view, principal, request) : ReadRemoteTransferTargetFailure(view, principal, request);
    }

    private RemoteTransferCoordinationReadResult ReadRemoteTransferSourceAttempt(IKeyValueView view,
        PrincipalRecord principal, RemoteTransferCoordinationReadRequest request)
    {
        var key = RemoteTransferStorage.IntentKey(request.Source, request.TransferId);
        var bytes = view.ReadOwnedValue(key) ?? throw Errors.Fail(ErrorCode.NotFound, RemoteTransferAttemptProtocol.Unavailable);
        var hint = ReadTransferCoordinationHint(view, key, bytes, principal.Id)
            ?? throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale);
        if (hint.Destination != request.Destination || hint.IntentDigest != RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken)
            || hint.AcceptGeneration != request.ExpectedGeneration
            || hint.AcceptPolicyGeneration != request.PolicyGeneration || hint.CompleteGeneration != request.CompleteGeneration
            || RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept) != request.AcceptCommandId)
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale); }
        return new(hint, null, null);
    }
}

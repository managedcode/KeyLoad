using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingQueriesRemoteTransfer operations against the caller's database context.</summary>
public static class MessagingQueriesRemoteTransfer
{
    internal static RemoteTransferCoordinationReadResult ReadRemoteTransferCoordination(this DatabaseEngine engine, string subject, RemoteTransferCoordinationReadRequest request, CancellationToken token)
    {
        RemoteTransferAttemptReadShape.Require(request);
        using var budget = RemoteTransferPendingBudget.Coordination(engine, token);
        var result = engine.Store.Read(view => engine.ReadRemoteTransferCoordination(budget.View(view), subject, request));
        budget.Result(result);
        return result;
    }

    internal static RemoteTransferCoordinationReadResult ReadRemoteTransferCoordination(this DatabaseEngine engine, IKeyValueView view, string subject, RemoteTransferCoordinationReadRequest request)
    {
        RemoteTransferValidation.ValidateTransferPair(request.Source, request.Destination);
        var principal = engine.Principal(view, subject, engine.Clock.GetUtcNow());
        var sourceRead = request.Purpose == RemoteTransferAttemptProtocol.SourceReadPurpose || request.Purpose == RemoteTransferRepairProtocol.ReadPurpose && request.RepairStage == QueueTransferRepairStage.Complete;
        var lane = sourceRead ? request.Source : request.Destination;
        if (principal.TenantId != lane.Partition.TenantId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, RemoteTransferAttemptProtocol.Invalid);
        }

        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, lane, Capability.QueueInspect);
        engine.RequireTransferCapabilities(principal, lane, Capability.QueuePublish);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferInspector(principal, lane, resource);
        engine.RequireTransferPublisher(principal, lane, resource);
        if (request.Purpose == RemoteTransferRepairProtocol.ReadPurpose)
        {
            return engine.ReadRemoteTransferRepairFailure(view, principal, request);
        }

        return request.Purpose == RemoteTransferAttemptProtocol.SourceReadPurpose ? engine.ReadRemoteTransferSourceAttempt(view, principal, request) : engine.ReadRemoteTransferTargetFailure(view, principal, request);
    }

    internal static RemoteTransferCoordinationReadResult ReadRemoteTransferSourceAttempt(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, RemoteTransferCoordinationReadRequest request)
    {
        var key = RemoteTransferStorage.IntentKey(request.Source, request.TransferId);
        var bytes = view.ReadOwnedValue(key) ?? throw Errors.Fail(ErrorCode.NotFound, RemoteTransferAttemptProtocol.Unavailable);
        var hint = engine.ReadTransferCoordinationHint(view, key, bytes, principal.Id) ?? throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale);
        if (hint.Destination != request.Destination || hint.IntentDigest != RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken) || hint.AcceptGeneration != request.ExpectedGeneration || hint.AcceptPolicyGeneration != request.PolicyGeneration || hint.CompleteGeneration != request.CompleteGeneration || RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept) != request.AcceptCommandId)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale);
        }

        return new(hint, null, null);
    }
}

using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferRepairAuthorization operations against the caller's database context.</summary>
public static class RemoteTransferRepairAuthorization
{
    internal static void AuthorizeAdvanceQueueTransferRepair(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, AdvanceQueueTransferRepair request)
    {
        RemoteTransferInspection.ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        RemoteTransferRepairIdentity.RequireGenerations(request.ExpectedCapacityGeneration, request.ExpectedPolicyGeneration, request.ExpectedCompleteGeneration);
        if (!Enum.IsDefined(request.Stage) || string.IsNullOrEmpty(request.FailureWitness))
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid);
        }

        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueueInspect);
        engine.RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueuePublish);
        var resource = engine.Resource(view, partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferInspector(principal, request.SourceQueue, resource);
        engine.RequireTransferPublisher(principal, request.SourceQueue, resource);
    }

    internal static void ReauthorizeAdvanceQueueTransferRepair(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, AdvanceQueueTransferRepair request)
    {
        engine.AuthorizeAdvanceQueueTransferRepair(view, principal, partition, request);
        var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId)) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
        engine.ValidateIntentRecord(view, record, request.SourceQueue, request.TransferId);
        if (record.PrincipalId != principal.Id || record.Repairs is not { } state || !state.History.Any(item => item.Stage == request.Stage && item.CapacityGeneration == request.ExpectedCapacityGeneration && item.PolicyGeneration == request.ExpectedPolicyGeneration && item.CompleteGeneration == request.ExpectedCompleteGeneration && item.WitnessToken == request.FailureWitness))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Invalid);
        }
    }

    internal static void ObserveRemoteTransferRepairDestination(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, AdvanceQueueTransferRepair request)
    {
        var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId)) ?? throw Errors.Fail(ErrorCode.NotFound, RemoteTransferRepairProtocol.Unavailable);
        engine.ValidateIntentRecord(view, record, request.SourceQueue, request.TransferId);
        if (record.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale);
        }

        engine.RequireTransferCapabilities(principal, record.Destination, Capability.QueueInspect);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, record.Destination.Partition, record.Destination.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferInspector(principal, record.Destination, resource);
    }
}

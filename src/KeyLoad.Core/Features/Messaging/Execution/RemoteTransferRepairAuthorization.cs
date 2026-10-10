using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void AuthorizeAdvanceQueueTransferRepair(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, AdvanceQueueTransferRepair request)
    {
        ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        RemoteTransferRepairIdentity.RequireGenerations(request.ExpectedCapacityGeneration,
            request.ExpectedPolicyGeneration, request.ExpectedCompleteGeneration);
        if (!Enum.IsDefined(request.Stage) || string.IsNullOrEmpty(request.FailureWitness))
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid); }
        RequireTransferAdministrator(principal);
        var resource = Resource(view, partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        RequireTransferInspector(principal, request.SourceQueue, resource);
        RequireTransferPublisher(principal, request.SourceQueue, resource);
    }

    private void ReauthorizeAdvanceQueueTransferRepair(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, AdvanceQueueTransferRepair request)
    {
        AuthorizeAdvanceQueueTransferRepair(view, principal, partition, request);
        var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
        ValidateIntentRecord(view, record, request.SourceQueue, request.TransferId);
        if (record.PrincipalId != principal.Id || record.Repairs is not { } state
            || !state.History.Any(item => item.Stage == request.Stage
                && item.CapacityGeneration == request.ExpectedCapacityGeneration
                && item.PolicyGeneration == request.ExpectedPolicyGeneration
                && item.CompleteGeneration == request.ExpectedCompleteGeneration
                && item.WitnessToken == request.FailureWitness))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Invalid); }
    }
}

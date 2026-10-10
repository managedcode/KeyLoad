using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void AuthorizeAdvanceQueueTransferAttempt(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, AdvanceQueueTransferAttempt request)
    {
        ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        if (request.ExpectedGeneration < RemoteTransferAttemptProtocol.FirstGeneration || string.IsNullOrEmpty(request.FailureWitness))
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferAttemptProtocol.Invalid); }
        RequireTransferAdministrator(principal);
        var resource = Resource(view, partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        RequireTransferInspector(principal, request.SourceQueue, resource);
        RequireTransferPublisher(principal, request.SourceQueue, resource);
    }

    private void ReauthorizeAdvanceQueueTransferAttempt(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, AdvanceQueueTransferAttempt request)
    {
        AuthorizeAdvanceQueueTransferAttempt(view, principal, partition, request);
        var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        ValidateIntentRecord(view, record, request.SourceQueue, request.TransferId);
        if (record.PrincipalId != principal.Id || record.Attempts is not { } state
            || !state.History.Any(item => item.Generation == request.ExpectedGeneration && item.WitnessToken == request.FailureWitness))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Invalid); }
    }
}

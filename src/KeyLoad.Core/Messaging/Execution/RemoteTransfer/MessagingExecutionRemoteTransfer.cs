using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingExecutionRemoteTransfer operations against the caller's database context.</summary>
public static class MessagingExecutionRemoteTransfer
{
    internal static void AuthorizeAdvanceQueueTransferAttempt(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, AdvanceQueueTransferAttempt request)
    {
        MessagingExecutionRemoteTransferRemoteTransferInspection.ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        if (request.ExpectedGeneration < RemoteTransferAttemptProtocol.FirstGeneration || string.IsNullOrEmpty(request.FailureWitness))
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferAttemptProtocol.Invalid);
        }

        MessagingExecutionRemoteTransferRemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueueInspect);
        engine.RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueuePublish);
        var resource = engine.Resource(view, partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferInspector(principal, request.SourceQueue, resource);
        engine.RequireTransferPublisher(principal, request.SourceQueue, resource);
    }

    internal static void ReauthorizeAdvanceQueueTransferAttempt(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, AdvanceQueueTransferAttempt request)
    {
        engine.AuthorizeAdvanceQueueTransferAttempt(view, principal, partition, request);
        var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId)) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        engine.ValidateIntentRecord(view, record, request.SourceQueue, request.TransferId);
        if (record.PrincipalId != principal.Id || record.Attempts is not { } state || !state.History.Any(item => item.Generation == request.ExpectedGeneration && item.WitnessToken == request.FailureWitness))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Invalid);
        }
    }
}

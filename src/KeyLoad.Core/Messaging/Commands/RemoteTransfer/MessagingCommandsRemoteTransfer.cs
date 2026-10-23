using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsRemoteTransfer operations against the caller's database context.</summary>
public static class MessagingCommandsRemoteTransfer
{
    internal static MutationReceipt ApplyAdvanceQueueTransferAttempt(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AdvanceQueueTransferAttempt request)
    {
        engine.AuthorizeAdvanceQueueTransferAttempt(tx, principal, partition, request);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var record = tx.GetRecord<RemoteTransferIntentRecord>(key) ?? throw Errors.Fail(ErrorCode.NotFound, RemoteTransferAttemptProtocol.Unavailable);
        engine.ValidateIntentRecord(tx, record, request.SourceQueue, request.TransferId);
        var state = record.Attempts;
        if (record.Repairs is { AcceptPolicyGeneration: > RemoteTransferRepairProtocol.InitialGeneration })
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferRepairProtocol.Unavailable);
        }

        if (state is null || engine.Limits.MaxQueueTransferAcceptAttempts is null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferAttemptProtocol.Unavailable);
        }

        if (record.State != QueueTransferState.OutputPending || record.PrincipalId != principal.Id || state.Generation != request.ExpectedGeneration)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale);
        }

        var witness = engine.Verify<RemoteTransferAcceptFailureClaims>(request.FailureWitness, engine.Limits.MaxBatchBytes);
        var hint = new RemoteTransferCoordinationHint(record.Source, record.Destination, record.TransferId, record.PrincipalId, record.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(record.IntentToken), engine.Token(tx, partition, engine.Store.Position), state.Generation, state.Ceiling);
        RemoteTransferFailureWitness.RequireRemoteTransferFailureWitness(witness, hint);
        if (!RemoteTransferCapacityRepair.Improved(witness))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale);
        }

        var nextGeneration = checked(state.Generation + RemoteTransferAttemptProtocol.FirstGeneration);
        if (nextGeneration > Math.Min(state.Ceiling, engine.Limits.MaxQueueTransferAcceptAttempts.Value))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteTransferAttemptProtocol.Capacity);
        }

        var reference = new RemoteTransferAcceptFailureReference(state.Generation, witness.OriginalAuthority.AcceptCommandId, witness.OutcomeDigest, request.FailureWitness);
        var updated = record with
        {
            Attempts = state with
            {
                Generation = nextGeneration,
                History = state.History.Add(reference)
            }
        };
        var capacity = RemoteTransferStorage.RequireSourceCounter(tx, request.SourceQueue);
        var next = RemoteTransferStorage.Replace(capacity, RemoteTransferStorage.SourceAccountedBytes(record), RemoteTransferStorage.SourceAccountedBytes(updated), engine.Limits);
        if (next.StoredRecords >= engine.Limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteTransferAttemptProtocol.Capacity);
        }

        next = next with
        {
            StoredRecords = checked(next.StoredRecords + RemoteTransferAttemptProtocol.FirstGeneration)
        };
        tx.PutRecord(key, updated);
        tx.PutRecord(RemoteTransferStorage.SourceCapacityKey(request.SourceQueue), next);
        return RemoteTransferCommands.TransferMutationReceipt(RemoteTransferAttemptProtocol.AdvanceKind, request.SourceQueue, request.TransferId, nextGeneration);
    }
}

using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal MutationReceipt ApplyAdvanceQueueTransferAttempt(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, AdvanceQueueTransferAttempt request)
    {
        AuthorizeAdvanceQueueTransferAttempt(tx, principal, partition, request);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var record = tx.GetRecord<RemoteTransferIntentRecord>(key)
            ?? throw Errors.Fail(ErrorCode.NotFound, RemoteTransferAttemptProtocol.Unavailable);
        ValidateIntentRecord(tx, record, request.SourceQueue, request.TransferId);
        var state = record.Attempts;
        if (record.Repairs is { AcceptPolicyGeneration: > RemoteTransferRepairProtocol.InitialGeneration })
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferRepairProtocol.Unavailable); }
        if (state is null || Limits.MaxQueueTransferAcceptAttempts is null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferAttemptProtocol.Unavailable); }
        if (record.State != QueueTransferState.OutputPending || record.PrincipalId != principal.Id
            || state.Generation != request.ExpectedGeneration)
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale); }
        var witness = Verify<RemoteTransferAcceptFailureClaims>(request.FailureWitness, Limits.MaxBatchBytes);
        var hint = new RemoteTransferCoordinationHint(record.Source, record.Destination, record.TransferId,
            record.PrincipalId, record.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(record.IntentToken),
            Token(tx, partition, Store.Position), state.Generation, state.Ceiling);
        RequireRemoteTransferFailureWitness(witness, hint);
        if (!RemoteTransferCapacityRepair.Improved(witness))
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale); }
        var nextGeneration = checked(state.Generation + RemoteTransferAttemptProtocol.FirstGeneration);
        if (nextGeneration > Math.Min(state.Ceiling, Limits.MaxQueueTransferAcceptAttempts.Value))
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteTransferAttemptProtocol.Capacity); }
        var reference = new RemoteTransferAcceptFailureReference(state.Generation, witness.OriginalAuthority.AcceptCommandId,
            witness.OutcomeDigest, request.FailureWitness);
        var updated = record with { Attempts = state with { Generation = nextGeneration, History = state.History.Add(reference) } };
        var capacity = RemoteTransferStorage.RequireSourceCounter(tx, request.SourceQueue);
        var next = RemoteTransferStorage.Replace(capacity, RemoteTransferStorage.SourceAccountedBytes(record),
            RemoteTransferStorage.SourceAccountedBytes(updated), Limits);
        if (next.StoredRecords >= Limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteTransferAttemptProtocol.Capacity); }
        next = next with { StoredRecords = checked(next.StoredRecords + RemoteTransferAttemptProtocol.FirstGeneration) };
        tx.PutRecord(key, updated);
        tx.PutRecord(RemoteTransferStorage.SourceCapacityKey(request.SourceQueue), next);
        return TransferMutationReceipt(RemoteTransferAttemptProtocol.AdvanceKind, request.SourceQueue, request.TransferId, nextGeneration);
    }
}

using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal MutationReceipt ApplyAdvanceQueueTransferRepair(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, AdvanceQueueTransferRepair request)
    {
        AuthorizeAdvanceQueueTransferRepair(tx, principal, partition, request);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var record = tx.GetRecord<RemoteTransferIntentRecord>(key)
            ?? throw Errors.Fail(ErrorCode.NotFound, RemoteTransferRepairProtocol.Unavailable);
        ValidateIntentRecord(tx, record, request.SourceQueue, request.TransferId);
        var state = record.Repairs;
        if (state is null || Limits.MaxQueueTransferRepairAttempts is null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferRepairProtocol.Unavailable); }
        var generation = record.Attempts?.Generation ?? RemoteTransferRepairProtocol.InitialGeneration;
        if (record.PrincipalId != principal.Id || record.State != QueueTransferState.OutputPending
            || generation != request.ExpectedCapacityGeneration || state.AcceptPolicyGeneration != request.ExpectedPolicyGeneration
            || state.CompleteGeneration != request.ExpectedCompleteGeneration)
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale); }
        var hint = new RemoteTransferCoordinationHint(record.Source, record.Destination, record.TransferId,
            record.PrincipalId, record.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(record.IntentToken),
            Token(tx, partition, Store.Position), generation, record.Attempts?.Ceiling,
            state.AcceptPolicyGeneration, state.CompleteGeneration, state.Ceiling);
        var witness = Verify<RemoteTransferRepairClaims>(request.FailureWitness, Limits.MaxBatchBytes);
        RequireRemoteTransferRepairWitness(witness, hint);
        if (witness.Stage != request.Stage || witness.CurrentPolicyEpoch != principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale); }
        RequireCurrentRemoteTransferRepair(tx, principal, record, hint, witness);
        if (state.History.Length >= Math.Min(state.Ceiling, Limits.MaxQueueTransferRepairAttempts.Value)
            - RemoteTransferRepairProtocol.InitialGeneration)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteTransferRepairProtocol.Capacity); }
        var reference = new RemoteTransferRepairReference(request.Stage, generation, state.AcceptPolicyGeneration,
            state.CompleteGeneration, witness.FailedCommandId, witness.CommandFingerprint, witness.OutcomeDigest, request.FailureWitness);
        var nextState = state with { History = state.History.Add(reference) };
        nextState = request.Stage == QueueTransferRepairStage.Accept
            ? nextState with { AcceptPolicyGeneration = checked(state.AcceptPolicyGeneration + RemoteTransferRepairProtocol.InitialGeneration) }
            : nextState with { CompleteGeneration = checked(state.CompleteGeneration + RemoteTransferRepairProtocol.InitialGeneration) };
        var updated = record with { Repairs = nextState };
        var current = RemoteTransferStorage.RequireSourceCounter(tx, request.SourceQueue);
        var next = RemoteTransferStorage.Replace(current, RemoteTransferStorage.SourceAccountedBytes(record),
            RemoteTransferStorage.SourceAccountedBytes(updated), Limits);
        if (next.StoredRecords >= Limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteTransferRepairProtocol.Capacity); }
        next = next with { StoredRecords = checked(next.StoredRecords + RemoteTransferRepairProtocol.InitialGeneration) };
        tx.PutRecord(key, updated);
        tx.PutRecord(RemoteTransferStorage.SourceCapacityKey(request.SourceQueue), next);
        return TransferMutationReceipt(RemoteTransferRepairProtocol.AdvanceKind, request.SourceQueue, request.TransferId,
            request.Stage == QueueTransferRepairStage.Accept ? nextState.AcceptPolicyGeneration : nextState.CompleteGeneration);
    }
}

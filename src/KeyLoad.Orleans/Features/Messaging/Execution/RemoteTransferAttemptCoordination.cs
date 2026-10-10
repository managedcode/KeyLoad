using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Orleans;

internal static class RemoteTransferAttemptCoordination
{
    internal static async Task<RemoteTransferCoordinationHint> RefreshAsync(RemoteTransferSignedExecution execution,
        RemoteTransferCoordinationHint hint, string intentToken, CancellationToken token)
    {
        if (hint.AcceptAttemptCeiling is null && hint.RepairCeiling is null)
        { return hint; }
        var value = await ReadAsync(execution, hint, intentToken, RemoteTransferAttemptProtocol.SourceReadPurpose,
            token).ConfigureAwait(true);
        var current = value.SourceState ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        if (current.Source != hint.Source || current.Destination != hint.Destination || current.TransferId != hint.TransferId
            || current.PrincipalId != hint.PrincipalId || current.Fingerprint != hint.Fingerprint
            || current.IntentDigest != hint.IntentDigest || current.SourceCut.Incarnation != hint.SourceCut.Incarnation
            || current.AcceptGeneration != hint.AcceptGeneration || current.AcceptAttemptCeiling != hint.AcceptAttemptCeiling
            || current.AcceptPolicyGeneration != hint.AcceptPolicyGeneration || current.CompleteGeneration != hint.CompleteGeneration
            || current.RepairCeiling != hint.RepairCeiling)
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferAttemptProtocol.Stale); }
        return current;
    }

    internal static async Task AdvanceAsync(RemoteTransferSignedExecution execution, DatabaseEngine database, RemoteTransferCoordinationHint hint,
        string intentToken, CancellationToken token)
    {
        if (hint.AcceptAttemptCeiling is not { } ceiling || hint.AcceptGeneration >= ceiling
            || hint.AcceptPolicyGeneration != RemoteTransferRepairProtocol.InitialGeneration)
        { return; }
        var value = await ReadAsync(execution, hint, intentToken, RemoteTransferAttemptProtocol.FailureReadPurpose,
            token).ConfigureAwait(true);
        if (value.TargetReceipt is not null)
        { return; }
        var witness = value.FailureWitness ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        var claims = database.Verify<RemoteTransferAcceptFailureClaims>(witness, database.Limits.MaxBatchBytes);
        var digest = claims.OutcomeDigest;
        var id = RemoteTransferAttemptIdentity.AdvanceId(hint, hint.AcceptGeneration, digest);
        var reply = await execution.ApplyAsync(hint.PrincipalId, new(id, hint.Source.Partition,
            [new AdvanceQueueTransferAttempt(hint.Source, hint.TransferId, hint.AcceptGeneration, witness)]), token).ConfigureAwait(true);
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? RemoteTransferAttemptProtocol.Unavailable); }
    }

    private static async Task<RemoteTransferCoordinationReadResult> ReadAsync(RemoteTransferSignedExecution execution,
        RemoteTransferCoordinationHint hint, string intentToken, string purpose, CancellationToken token)
    {
        var request = new RemoteTransferCoordinationReadRequest(purpose, hint.Source, hint.Destination,
            hint.TransferId, intentToken, hint.AcceptGeneration, RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
            PolicyGeneration: hint.AcceptPolicyGeneration, CompleteGeneration: hint.CompleteGeneration);
        var result = await execution.ReadAsync<RemoteTransferCoordinationReadResult>(hint.PrincipalId,
            GrainReadKind.QueueTransferCoordination, NativeSerialization.Serialize(request), token).ConfigureAwait(true)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferAttemptProtocol.Unavailable);
        var valid = purpose == RemoteTransferAttemptProtocol.SourceReadPurpose
            ? result.SourceState is not null && result.FailureWitness is null && result.TargetReceipt is null
            : result.SourceState is null && ((result.FailureWitness is not null) != (result.TargetReceipt is not null));
        valid = valid && result.RepairWitness is null;
        if (!valid)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferAttemptProtocol.Invalid); }
        return result;
    }
}

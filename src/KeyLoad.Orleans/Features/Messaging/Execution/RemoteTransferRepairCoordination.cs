using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Orleans;

internal static class RemoteTransferRepairCoordination
{
    internal static async Task AdvanceAsync(RemoteTransferSignedExecution execution, DatabaseEngine database,
        RemoteTransferCoordinationHint hint, string intentToken, QueueTransferRepairStage stage,
        string? receiptToken, CancellationToken token)
    {
        if (hint.RepairCeiling is not { } ceiling || checked(hint.AcceptPolicyGeneration + hint.CompleteGeneration
            - RemoteTransferRepairProtocol.InitialGeneration) >= ceiling)
        { return; }
        var request = new RemoteTransferCoordinationReadRequest(RemoteTransferRepairProtocol.ReadPurpose,
            hint.Source, hint.Destination, hint.TransferId, intentToken, hint.AcceptGeneration,
            RemoteTransferRepairIdentity.CommandId(hint, stage), stage, hint.AcceptPolicyGeneration,
            hint.CompleteGeneration, receiptToken);
        var value = await execution.ReadAsync<RemoteTransferCoordinationReadResult>(hint.PrincipalId,
            GrainReadKind.QueueTransferCoordination, NativeSerialization.Serialize(request), token).ConfigureAwait(true)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
        if (stage == QueueTransferRepairStage.Accept && value.TargetReceipt is not null
            && value.SourceState is null && value.FailureWitness is null && value.RepairWitness is null)
        { return; }
        if (value.SourceState is not null || value.FailureWitness is not null || value.TargetReceipt is not null
            || value.RepairWitness is null)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid); }
        var witness = database.Verify<RemoteTransferRepairClaims>(value.RepairWitness, database.Limits.MaxBatchBytes);
        var id = RemoteTransferRepairIdentity.AdvanceId(hint, stage, witness.OutcomeDigest);
        var reply = await execution.ApplyAsync(hint.PrincipalId, new(id, hint.Source.Partition,
            [new AdvanceQueueTransferRepair(hint.Source, hint.TransferId, stage, hint.AcceptGeneration,
                hint.AcceptPolicyGeneration, hint.CompleteGeneration, value.RepairWitness)]), token).ConfigureAwait(true);
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? RemoteTransferRepairProtocol.Unavailable); }
    }
}

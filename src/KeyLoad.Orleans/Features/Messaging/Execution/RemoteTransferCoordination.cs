using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class RemoteTransferCoordination
{
    internal static async Task<DueDispatchResult> ExecuteAsync(RemoteTransferCoordinationHint hint, string owner,
        DatabaseEngine database, ICommitCoordinator coordinator, RemoteTransferSignedExecution execution, TimeProvider clock,
        IOptions<DueCoordinationOptions> options, RuntimeJournalAdmission admission, CancellationToken cancellationToken)
    {
        var subject = options.Value.TransferCoordinatorPrincipalId
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferCoordinationProtocol.Unavailable);
        RequireHint(hint, owner, subject, database.Store.Identity.Incarnation);
        using var timeout = new CancellationTokenSource(options.Value.DispatchDeadline, clock);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, admission.SchedulingToken, timeout.Token);
        await coordinator.ReadBarrierAsync(lifetime.Token).ConfigureAwait(true);
        var source = await SourceAsync(execution, hint, lifetime.Token).ConfigureAwait(true);
        if (source is null || source.State != QueueTransferState.OutputPending)
        { return new(null); }
        hint = await RemoteTransferAttemptCoordination.RefreshAsync(execution, hint, source.IntentToken, lifetime.Token).ConfigureAwait(true);
        var receipt = await TargetAsync(execution, hint, lifetime.Token).ConfigureAwait(true);
        if (receipt is null)
        {
            var id = RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept);
            var reply = await execution.ApplyAsync(subject, new(id, hint.Destination.Partition,
                [new AcceptQueueTransfer(hint.Destination, source.IntentToken)]), lifetime.Token).ConfigureAwait(true);
            if (reply.Error is { } error)
            {
                if (hint.AcceptAttemptCeiling is not null && error == ErrorCode.ResourceExhausted)
                {
                    await RemoteTransferAttemptCoordination.AdvanceAsync(execution, database, hint, source.IntentToken,
                        lifetime.Token).ConfigureAwait(true);
                }
                if (hint.RepairCeiling is not null && error == ErrorCode.PermissionDenied)
                {
                    await RemoteTransferRepairCoordination.AdvanceAsync(execution, database, hint, source.IntentToken,
                        QueueTransferRepairStage.Accept, null, lifetime.Token).ConfigureAwait(true);
                }
                return new(error);
            }
            receipt = await TargetAsync(execution, hint, lifetime.Token).ConfigureAwait(true);
        }
        if (receipt is null)
        { return new(ErrorCode.UnknownWriteOutcome); }
        return await CompleteAsync(execution, database, hint, receipt, lifetime.Token).ConfigureAwait(true);
    }

    private static async Task<DueDispatchResult> CompleteAsync(RemoteTransferSignedExecution execution, DatabaseEngine database,
        RemoteTransferCoordinationHint hint, QueueTransferReceiptInspection receipt, CancellationToken token)
    {
        var source = await SourceAsync(execution, hint, token).ConfigureAwait(true);
        if (source is null || source.State != QueueTransferState.OutputPending)
        { return new(null); }
        var current = await TargetAsync(execution, hint, token).ConfigureAwait(true);
        if (current != receipt)
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferCoordinationProtocol.InvalidResult); }
        var id = RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete);
        var reply = await execution.ApplyAsync(hint.PrincipalId, new(id, hint.Source.Partition,
            [new CompleteQueueTransfer(hint.Source, hint.TransferId, receipt.ReceiptToken)]), token).ConfigureAwait(true);
        if (hint.RepairCeiling is not null && reply.Error == ErrorCode.PermissionDenied)
        {
            await RemoteTransferRepairCoordination.AdvanceAsync(execution, database, hint, source.IntentToken,
                QueueTransferRepairStage.Complete, receipt.ReceiptToken, token).ConfigureAwait(true);
        }
        return new(reply.Error);
    }

    private static async Task<QueueTransferInspection?> SourceAsync(RemoteTransferSignedExecution execution,
        RemoteTransferCoordinationHint hint, CancellationToken token)
    {
        var source = await execution.ReadAsync<QueueTransferInspection>(hint.PrincipalId, GrainReadKind.QueueTransfer,
            NativeSerialization.Serialize(new InspectQueueTransferRequest(hint.Source, hint.TransferId)), token).ConfigureAwait(true);
        if (source is not null && (source.SourceQueue != hint.Source || source.Destination != hint.Destination
            || source.TransferId != hint.TransferId || RemoteTransferCoordinationIdentity.IntentDigest(source.IntentToken) != hint.IntentDigest))
        { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferCoordinationProtocol.InvalidResult); }
        return source;
    }

    private static async Task<QueueTransferReceiptInspection?> TargetAsync(RemoteTransferSignedExecution execution,
        RemoteTransferCoordinationHint hint, CancellationToken token)
    {
        var receipt = await execution.ReadAsync<QueueTransferReceiptInspection>(hint.PrincipalId, GrainReadKind.QueueTransferReceipt,
            NativeSerialization.Serialize(new InspectQueueTransferReceiptRequest(hint.Destination, hint.Source, hint.TransferId)), token).ConfigureAwait(true);
        if (receipt is not null && (receipt.SourceQueue != hint.Source || receipt.DestinationQueue != hint.Destination || receipt.TransferId != hint.TransferId))
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferCoordinationProtocol.InvalidResult); }
        return receipt;
    }

    private static void RequireHint(RemoteTransferCoordinationHint hint, string owner, string subject, Guid incarnation)
    {
        if (hint is null || hint.Source?.Partition is null || hint.Destination?.Partition is null || hint.SourceCut is null
            || hint.Source.Partition.AtomicPartitionId != owner || hint.PrincipalId != subject || hint.TransferId == Guid.Empty
            || hint.SourceCut.Incarnation != incarnation || hint.SourceCut.AtomicPartitionId != owner)
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferCoordinationProtocol.InvalidHint); }
        DatabaseEngine.ValidatePartition(hint.Source.Partition);
        DatabaseEngine.ValidatePartition(hint.Destination.Partition);
        JsonData.Identifier(hint.Fingerprint);
        JsonData.Identifier(hint.IntentDigest);
        JsonData.Identifier(subject);
        JsonData.Identifier(hint.Source.Queue);
        JsonData.Identifier(hint.Destination.Queue);
    }
}

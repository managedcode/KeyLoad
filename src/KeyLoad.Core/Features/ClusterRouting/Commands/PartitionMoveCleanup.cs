using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MoveCleanupMetadataMutations = 3;
    private const int MoveCleanupRecordMutations = 3;

    private PartitionMovePhaseResult ExecuteMoveCleanup(IAtomicTransaction transaction,
        Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(phase.Body.Span);
        RequireMovePhaseIdentity(phase, body.Control);
        RequireMoveCleanupRole(transaction, body, phase);
        var retained = PartitionMoveCleanupStorage.Read(transaction, phase.Partition, phase.MoveId,
            Limits.MaxBatchBytes);
        if (phase.Stage == PartitionMovePeerStage.Abort && body.Role == PartitionMoveCleanupRole.Source
            && retained is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var state = PartitionMoveCleanupStorage.Read(transaction, phase.Partition, phase.MoveId,
            Limits.MaxBatchBytes) ?? new(PartitionMoveProtocol.Version, phase.MoveId, phase.Partition,
                phase.ControlIntentDigest, phase.Stage, body.Role,
                phase.Stage == PartitionMovePeerStage.Abort && body.Role == PartitionMoveCleanupRole.Source
                    ? PartitionMoveCleanupFamilies.All.Length : PartitionMoveProtocol.EmptyCount,
                null, PartitionMoveProtocol.EmptyCount);
        if (state.Stage != phase.Stage || state.Role != body.Role
            || state.ControlIntentDigest != phase.ControlIntentDigest || state.Completion is not null
            || state.NextFamily != body.FamilyOrdinal || state.NextBatch != phase.PageOrdinal)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (phase.Resources.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        var journal = MoveJournalReceipt(transaction, commandId, position, phase.ControlIntentDigest);
        if (body.FamilyOrdinal == PartitionMoveCleanupFamilies.All.Length)
        { state = CompleteMoveLocalCleanup(transaction, phase, body, state, journal); }
        else
        { state = DeleteMoveCleanupPage(transaction, phase, state); }
        PartitionMoveTargetStorage.Write(transaction, PartitionMoveCleanupStorage.Key(phase.Partition,
            phase.MoveId), state, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, journal, null, null, null, null, null, state);
    }

    private void RequireMoveCleanupRole(IKeyValueView view, PartitionMoveCleanupBody body, PartitionMovePhaseCommand phase)
    {
        if (body.OperatorPrincipalId != body.Control.PrincipalId
            || phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(body.Control)
            || !Enum.IsDefined(body.Role) || body.FamilyOrdinal < PartitionMoveProtocol.EmptyCount
            || body.FamilyOrdinal > PartitionMoveCleanupFamilies.All.Length
            || phase.Stage == PartitionMovePeerStage.Retire
                && (body.Control.Phase != PartitionMovePhase.Published || body.Role != PartitionMoveCleanupRole.Source)
            || phase.Stage == PartitionMovePeerStage.Abort && body.Control.Phase != PartitionMovePhase.Aborting
            || body.Role == PartitionMoveCleanupRole.Source && Store.Identity.Incarnation != body.Control.SourcePlacement.Incarnation
            || body.Role == PartitionMoveCleanupRole.Target && Store.Identity.Incarnation != body.Control.DestinationOwner.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireMoveCleanupOwner(view, body, phase);
        if (body.Role == PartitionMoveCleanupRole.Source && body.PrecedingGrantId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }

    }

    private PartitionMoveCleanupState DeleteMoveCleanupPage(IAtomicTransaction transaction,
        PartitionMovePhaseCommand phase, PartitionMoveCleanupState state)
    {
        var family = PartitionMoveCleanupFamilies.All[state.NextFamily];
        var advance = true;
        if (!(phase.Stage == PartitionMovePeerStage.Abort && state.Role == PartitionMoveCleanupRole.Source)
            && !PartitionMoveFamilyCapture.ControlOwned(family)
)
        {
            var maximum = (Limits.MaxBatchMutations - MoveCleanupMetadataMutations) / MoveCleanupRecordMutations;
            if (maximum <= PartitionMoveProtocol.EmptyCount)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
            var prefix = family == PartitionMoveProtocol.TargetPageSpace
                ? MoveCleanupPagePrefix(transaction, phase, state)
                : KeySpace.Partition(family, phase.Partition);
            var page = transaction.Scan(prefix, maximum);
            long bytes = PartitionMoveProtocol.EmptyCount;
            foreach (var record in page.Records)
            {
                bytes = checked(bytes + record.Key.Length + record.Value.Length);
                if (bytes > Limits.MaxBatchBytes)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
                PartitionMoveBlobImport.Release(transaction, family, record,
                    phase.Resources, Store.Identity.Incarnation);
                transaction.Delete(record.Key.ToArray());
            }
            advance = !page.HasMore;
        }
        return state with
        {
            NextFamily = advance ? state.NextFamily + PartitionMoveProtocol.SequenceStep
            : state.NextFamily,
            NextBatch = checked(state.NextBatch + PartitionMoveProtocol.SequenceStep)
        };
    }
}

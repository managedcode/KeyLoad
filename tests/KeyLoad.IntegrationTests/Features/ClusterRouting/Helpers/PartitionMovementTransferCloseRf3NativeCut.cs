using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Same stopped owners expose genuine join receipts, settled quota and unchanged complete business bytes.</summary>
internal static class PartitionMovementTransferCloseRf3NativeCut
{
    internal static async Task RequireJoinedAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementPublicParentRf3NativeCut[] before,
        PartitionMovementPublicParentRf3NativeCut[] after, PartitionMoveResult terminal)
    {
        foreach (var original in before)
        {
            var actual = after.Single(cut => cut.Node == original.Node);
            foreach (var family in PartitionRecordFamilies.All.Where(family => family is not
                (PartitionRecordFamilies.OutcomeV2 or PartitionRecordFamilies.OutcomeLocatorV2 or PartitionRecordFamilies.OutcomeLocator)))
            {
                var prefix = Convert.ToHexString(KeySpace.Partition(family, seed.Partition));
                await SqlRf3Protocol.EqualAsync(original.Rows.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray(),
                    actual.Rows.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray());
            }
            await Assert.That(actual.OutstandingMoveGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
            await Assert.That(actual.OutstandingDatabaseGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
            if (actual.Header is { } header)
            {
                await SqlRf3Protocol.EqualAsync(terminal, header.TerminalResult);
                await Assert.That(header.PendingOriginalPhaseCommandId).IsNull();
                await Assert.That(header.InterruptedOriginalPhaseCommandId).IsNull();
                await Assert.That(header.TerminalObservationReceipt).IsNotNull();
                var rows = PartitionMovementCapturePointerRf3Fault.Rows(actual);
                await Assert.That(rows.ContainsKey(Convert.ToHexString(PartitionMoveParentKeys.Active(seed.Partition)))).IsFalse();
                await Assert.That(Counter(rows, PartitionMoveParentKeys.PrincipalActive(header.OperatorPrincipalId)))
                    .IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
                await Assert.That(Counter(rows, PartitionMoveParentKeys.DatabaseActive(seed.Partition)))
                    .IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
                await Assert.That(actual.OutstandingPrincipalGrants).IsEqualTo((long?)PartitionMoveProtocol.EmptyCount);
                await RequireNoForwardAdmissionAsync(actual, seed.FirstRequest);
                await RequireSourceClosureAsync(actual, seed.FirstRequest);
                await RequireTargetFirstAsync(wave, actual, seed.FirstRequest);
                var sourceAbortId = PartitionMovementParentPhaseIds.For(seed.FirstRequest,
                    PartitionMovementPublicParentRf3Administrator.PrincipalId, PartitionMovementParentPhaseRole.SourceAbort);
                var sourceAbort = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, actual.Node,
                    seed.FirstRequest, sourceAbortId);
                await RequireTerminalSourceAbortAsync(sourceAbort, sourceAbortId);
            }
            var entries = PartitionMovementCapturePointerRf3Fault.AppliedEntries(wave, original, actual);
            await Assert.That(actual.StorePosition).IsGreaterThanOrEqualTo(original.StorePosition);
            var beforeApplied = PartitionMovementCapturePointerRf3Fault.Applied(original);
            var afterApplied = PartitionMovementCapturePointerRf3Fault.Applied(actual);
            await Assert.That(afterApplied - beforeApplied).IsEqualTo((long)entries.Length);
        }
    }

    private static async Task RequireSourceClosureAsync(PartitionMovementPublicParentRf3NativeCut cut,
        PartitionMoveRequest request)
    {
        var phase = cut.OriginalPhase ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var outcome = phase.OriginalResult ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(outcome.Error).IsNull();
        await Assert.That(phase.ObservationCheckpointReceipt).IsNotNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.SourceBeginAbort);
        await Assert.That(actual.MoveId).IsEqualTo(request.MoveId);
        await Assert.That(actual.Journal.CommandId).IsEqualTo(PartitionMovementTransferCloseRf3AbortAssertions.SourceClosureId(request));
        await Assert.That(actual.Journal.ControlIntentDigest).IsEqualTo(phase.OriginalPhase!.ControlIntentDigest);
        await Assert.That(actual.Journal.AppliedPosition).IsGreaterThan((long)PartitionMoveProtocol.EmptyCount);
        await Assert.That(actual.Journal.AppliedPosition).IsLessThanOrEqualTo(PartitionMovementCapturePointerRf3Fault.Applied(cut));
        await SqlRf3Protocol.EqualAsync(phase.OriginalReceiverOwner, actual.Journal.PhysicalOwner);
    }
    private static async Task RequireTerminalSourceAbortAsync(PartitionMovementPublicParentRf3NativeCut cut, Guid sourceAbortId)
    {
        var phase = cut.OriginalPhase ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var outcome = phase.OriginalResult ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(outcome.Error).IsNull();
        await Assert.That(phase.ObservationCheckpointReceipt).IsNotNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.Abort);
        await Assert.That(actual.Journal.CommandId).IsEqualTo(sourceAbortId);
        await Assert.That(actual.Cleanup!.Role).IsEqualTo(PartitionMoveCleanupRole.Source);
        await Assert.That(actual.Cleanup.Completion).IsNotNull();
        await Assert.That(actual.Cleanup.Completion!.CommandId).IsEqualTo(sourceAbortId);
    }

    private static async Task RequireNoForwardAdmissionAsync(PartitionMovementPublicParentRf3NativeCut cut,
        PartitionMoveRequest request)
    {
        var prefix = Convert.ToHexString(PartitionMoveParentKeys.PhasePrefix(request.Partition, request.MoveId));
        var rows = PartitionMovementCapturePointerRf3Fault.Rows(cut);
        foreach (var row in rows.Where(row => row.Key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var phase = NativeSerialization.Deserialize<PartitionMoveParentPhase>(Convert.FromHexString(row.Value));
            await Assert.That(phase.Stage is PartitionMovePeerStage.StagePage or PartitionMovePeerStage.Install
                or PartitionMovePeerStage.PublishWitness or PartitionMovePeerStage.Retire).IsFalse();
            if (phase.Stage == PartitionMovePeerStage.ControlAuthorize)
            {
                var body = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(phase.OriginalPhase!.Body.Span);
                await Assert.That(body.Phase.Stage is PartitionMovePeerStage.StagePage or PartitionMovePeerStage.Install
                    or PartitionMovePeerStage.Retire).IsFalse();
            }
        }
        await Assert.That(cut.SettledStagePages).IsEmpty();
    }

    private static async Task RequireTargetFirstAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3NativeCut closure, PartitionMoveRequest request)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(closure.OriginalPhase!.OriginalPhase!.Body.Span);
        var targetGrant = PartitionMovementCapturePointerRf3Fault.Read<PartitionMovePhaseGrant>(closure,
            PartitionMoveGrantStorage.Key(request.Partition, body.PrecedingGrantId));
        await Assert.That(targetGrant.Stage).IsEqualTo(PartitionMovePeerStage.Abort);
        await Assert.That(targetGrant.CleanupRole).IsEqualTo((PartitionMoveCleanupRole?)PartitionMoveCleanupRole.Target);
        await Assert.That(targetGrant.Settlement).IsNotNull();
        var retained = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, closure.Node, request,
            targetGrant.PhaseCommandId).OriginalPhase!;
        await Assert.That(retained.OriginalResult!.Error).IsNull();
        await Assert.That(retained.ObservationCheckpointReceipt).IsNotNull();
        var actual = retained.OriginalResult.Get<PartitionMovePhaseResult>();
        await Assert.That(actual.Cleanup!.Role).IsEqualTo(PartitionMoveCleanupRole.Target);
        await SqlRf3Protocol.EqualAsync(targetGrant.Settlement, actual.Cleanup.Completion);
        await SqlRf3Protocol.EqualAsync(targetGrant.Settlement, actual.Journal);
    }

    private static long Counter(Dictionary<string, string> rows, byte[] key)
        => rows.TryGetValue(Convert.ToHexString(key), out var bytes)
            ? NativeSerialization.Deserialize<long>(Convert.FromHexString(bytes)) : PartitionMoveProtocol.EmptyCount;
}

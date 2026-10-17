using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortCleanup
{
    internal static async Task<(PartitionMovePhaseResult State, PartitionMovePhaseResult Terminal, Guid GrantId, ReadOnlyMemory<byte> Body,
        ReplicatedOperation Original, OperationResult Outcome)> ExecuteAsync(
        D2ImportSession session, D2ImportImage aborted, D2ImportImage remaining, bool unknown, CancellationToken token)
    {
        var state = await D2ImportPhases.LocalAsync(session, D2ImportPhases.Phase(session, aborted.Captured,
            PartitionMovePeerStage.ControlBeginAbort, NativeSerialization.Serialize(new PartitionMoveControlBody(
                D2ImportCrashProtocol.Principal, aborted.Captured.Control!))), token);
        await Assert.That(state.Control!.Phase).IsEqualTo(PartitionMovePhase.Aborting);
        var marker = D2ImportAbortAssertions.Marker(session, aborted);
        var other = D2ImportAbortAssertions.Marker(session, remaining);
        await Assert.That(marker).IsNotNull();
        await Assert.That(other).IsNotNull();
        var family = D2ImportCrashProtocol.Empty;
        var batch = D2ImportCrashProtocol.Empty;
        var records = aborted.Pages.Sum(page =>
            NativeSerialization.Deserialize<PartitionMoveImagePage>(page.NativePage.Span).Records.Length);
        var maximum = checked(PartitionMoveCleanupFamilies.All.Length + records
            + aborted.Pages.Length + D2ImportCrashProtocol.Single);
        while (batch < maximum)
        {
            var body = NativeSerialization.Serialize(new PartitionMoveCleanupBody(D2ImportCrashProtocol.Principal,
                state.Control!, PartitionMoveCleanupRole.Target, family, Guid.Empty));
            var phase = D2ImportPhases.Phase(session, state, PartitionMovePeerStage.Abort, body, batch);
            var applied = await D2ImportPhases.EffectAsync(session, state, phase, atSource: false, token);
            await D2ImportAbortAssertions.OriginAsync(session, unknown);
            var cleanup = applied.Result.Cleanup
                ?? throw new InvalidOperationException("The actual target Abort cleanup state is absent.");
            await Assert.That(cleanup.Role).IsEqualTo(PartitionMoveCleanupRole.Target);
            await Assert.That(cleanup.MoveId).IsEqualTo(aborted.Captured.Control!.MoveId);
            await Assert.That(cleanup.Partition).IsEqualTo(aborted.Captured.Control.Partition);
            await D2ImportAbortAssertions.MarkerAsync(session, remaining, other);
            if (cleanup.Completion is not null)
            {
                await Assert.That(family).IsEqualTo(PartitionMoveCleanupFamilies.All.Length);
                await D2InventoryRecoveryPages.SameAsync(applied.Result.Journal, cleanup.Completion);
                await D2ImportAbortAssertions.MarkerAsync(session, aborted, null);
                await D2ImportAbortAssertions.DocumentAbsentAsync(session, aborted);
                await D2ImportInventory.BlockedAsync(session, token);
                return (state, applied.Result, applied.GrantId, body, applied.Original, applied.Outcome);
            }
            await D2ImportAbortAssertions.MarkerAsync(session, aborted, marker);
            await D2ImportInventory.BlockedAsync(session, token);
            family = cleanup.NextFamily;
            batch = cleanup.NextBatch;
        }
        throw new InvalidOperationException("The native bounded target Abort did not reach its terminal cleanup.");
    }
}

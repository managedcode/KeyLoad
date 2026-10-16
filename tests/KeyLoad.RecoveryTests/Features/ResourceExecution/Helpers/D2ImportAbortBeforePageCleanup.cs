using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortBeforePageCleanup
{
    internal static async Task RequireAbsentAsync(D2ImportSession session, D2ImportImage image)
    {
        var stage = session.Target.Store.Read(view => PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(image.Captured.Control!.Partition), session.Target.Database.Limits.MaxBatchBytes));
        await Assert.That(stage).IsNull();
        await D2ImportAbortAssertions.MarkerAsync(session, image, null);
        await D2ImportAbortAssertions.DocumentAbsentAsync(session, image);
    }

    internal static async Task<(PartitionMovePhaseResult State, PartitionMovePhaseResult Terminal, Guid GrantId,
        ReadOnlyMemory<byte> Body, ReplicatedOperation Original, OperationResult Outcome)> ExecuteAsync(
        D2ImportSession session, D2ImportImage aborted, D2ImportImage remaining, CancellationToken token)
    {
        var started = TimeProvider.System.GetTimestamp();
        await RequireAbsentAsync(session, aborted);
        var origin = session.Target.Store.Read(view => ResourceOwnerInventoryCatalog.ReadOrigin(view, session.Target.Database.Limits));
        var other = D2ImportAbortAssertions.Marker(session, remaining);
        await Assert.That(other).IsNotNull();
        var state = await D2ImportPhases.LocalAsync(session, D2ImportPhases.Phase(session, aborted.Captured,
            PartitionMovePeerStage.ControlBeginAbort, NativeSerialization.Serialize(new PartitionMoveControlBody(
                D2ImportCrashProtocol.Principal, aborted.Captured.Control!))), token);
        await Assert.That(state.Control!.Phase).IsEqualTo(PartitionMovePhase.Aborting);
        var family = D2ImportCrashProtocol.Empty;
        var batch = D2ImportCrashProtocol.Empty;
        var maximum = checked(PartitionMoveCleanupFamilies.All.Length + D2ImportCrashProtocol.Single);
        while (batch < maximum)
        {
            var body = NativeSerialization.Serialize(new PartitionMoveCleanupBody(D2ImportCrashProtocol.Principal,
                state.Control!, PartitionMoveCleanupRole.Target, family, Guid.Empty));
            var applied = await D2ImportAbortBeforePageObservation.EffectAsync(session, state,
                body, family, batch, started, token);
            await RequireAbsentAsync(session, aborted);
            var currentOrigin = session.Target.Store.Read(view =>
                ResourceOwnerInventoryCatalog.ReadOrigin(view, session.Target.Database.Limits));
            if (origin is null)
            {
                await Assert.That(currentOrigin).IsNull();
            }
            else
            {
                await Assert.That(currentOrigin).IsNotNull();
                await D2InventoryRecoveryPages.SameAsync(origin, currentOrigin!);
            }
            await D2ImportAbortAssertions.MarkerAsync(session, remaining, other);
            await D2ImportInventory.BlockedAsync(session, token);
            var cleanup = applied.Result.Cleanup
                ?? throw new InvalidOperationException("The original missing-stage target Abort returned no cleanup state.");
            if (cleanup.Completion is not null)
            {
                await D2InventoryRecoveryPages.SameAsync(applied.Result.Journal, cleanup.Completion);
                return (state, applied.Result, applied.GrantId, body, applied.Original, applied.Outcome);
            }
            family = cleanup.NextFamily;
            batch = cleanup.NextBatch;
        }
        throw new InvalidOperationException("The actual missing-stage target Abort did not settle within its empty image bound.");
    }
}

using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortFinalization
{
    internal static async Task ExecuteAsync(D2ImportSession session, PartitionMovePhaseResult state,
        PartitionMovePhaseResult targetTerminal, Guid targetGrant, ReadOnlyMemory<byte> targetBody,
        CancellationToken token)
    {
        var body = NativeSerialization.Serialize(new PartitionMoveCleanupBody(D2ImportCrashProtocol.Principal,
            state.Control!, PartitionMoveCleanupRole.Source, PartitionMoveCleanupFamilies.All.Length, targetGrant));
        // Both original source capture/page/release owners joined before this Abort began.
        _ = await D2ImportPhases.EffectAsync(session, state, D2ImportPhases.Phase(session, state,
            PartitionMovePeerStage.SourceBeginAbort, body), atSource: true, token);
        var source = await D2ImportPhases.EffectAsync(session, state, D2ImportPhases.Phase(session, state,
            PartitionMovePeerStage.Abort, body), atSource: true, token);
        var cleanup = source.Result.Cleanup
            ?? throw new InvalidOperationException("No actual terminal source Abort state returned.");
        await D2InventoryRecoveryPages.SameAsync(source.Result.Journal, cleanup.Completion);
        var completion = NativeSerialization.Serialize(new PartitionMoveCompletionBody(D2ImportCrashProtocol.Principal,
            state.Control!, source.Result.Journal, targetTerminal.Journal, source.GrantId, targetGrant, body, targetBody));
        var canceled = await D2ImportPhases.LocalAsync(session, D2ImportPhases.Phase(session, state,
            PartitionMovePeerStage.ControlCancelGrants, completion), token);
        await Assert.That(canceled.Cleanup!.Completion).IsNotNull();
        var final = await D2ImportPhases.LocalAsync(session, D2ImportPhases.Phase(session, state,
            PartitionMovePeerStage.ControlFinalizeAbort, completion), token);
        await Assert.That(final.Control!.Phase).IsEqualTo(PartitionMovePhase.Aborted);
        await Assert.That(final.Control.MoveId).IsEqualTo(state.Control!.MoveId);
        await Assert.That(final.Control.Partition).IsEqualTo(state.Control.Partition);
    }
}

using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Runs every abort effect from actual control grants while the original capture remains owned.</summary>
internal static class ControlledPartitionMovementActiveAbort
{
    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ServerRuntimeOptions sourceRuntime,
        ServerRuntimeOptions targetRuntime, PartitionMovementPeerAdmission sourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult acceptedFence, string callerAddress, DateTimeOffset expiry,
        CancellationToken cancellationToken)
    {
        PartitionMovePhaseResult? completed = null;
        await ControlledPartitionMovementActiveCapture.ExecuteAsync(source, sourceRuntime, sourceAdmission,
            corpus, acceptedFence, callerAddress, expiry, async (owner, original, handle) =>
            {
                await Assert.That(handle.Descriptor.Partition).IsEqualTo(ControlledPartitionMovementCorpus.Partition);
                var aborting = await ControlledPartitionMovementBeginAbortFlow.ExecuteAsync(source, sourceRuntime,
                    sourceAdmission, corpus, acceptedFence, callerAddress, expiry, cancellationToken);
                var targetTerminal = await ControlledPartitionMovementTargetAbortFlow.ExecuteAsync(source, target,
                    sourceRuntime, targetRuntime, sourceAdmission, targetAdmission, corpus, aborting,
                    callerAddress, expiry, cancellationToken);
                await ControlledPartitionMovementSourceAbortMarkerFlow.ExecuteAsync(source, sourceRuntime,
                    sourceAdmission, corpus, aborting, targetTerminal.GrantId, callerAddress, expiry,
                    cancellationToken);
                await ControlledPartitionMovementClosedCaptureAssertions.AssertAsync(source, owner, original,
                    cancellationToken);
                var sourceTerminal = await ControlledPartitionMovementSourceAbortFlow.ExecuteAsync(source, owner,
                    sourceRuntime, sourceAdmission, corpus, aborting, targetTerminal.GrantId, callerAddress,
                    expiry, cancellationToken);
                var control = aborting.Control
                    ?? throw new InvalidOperationException("The actual Aborting control is absent.");
                var completion = new PartitionMoveCompletionBody(PhysicalShardCatalogFixture.RootPrincipalId,
                    control, sourceTerminal.Terminal.Journal, targetTerminal.Terminal.Journal,
                    sourceTerminal.GrantId, targetTerminal.GrantId, sourceTerminal.OriginalBody,
                    targetTerminal.OriginalBody);
                completed = await ControlledPartitionMovementAbortCompletion.ExecuteAsync(source, sourceRuntime,
                    sourceAdmission, corpus, aborting, completion, callerAddress, expiry, cancellationToken);
            }, cancellationToken);
        return completed ?? throw new InvalidOperationException("Actual abort completion is absent.");
    }
}

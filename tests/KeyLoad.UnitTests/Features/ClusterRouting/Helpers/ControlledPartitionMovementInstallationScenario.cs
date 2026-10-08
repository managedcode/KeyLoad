using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Composes genuine capture, staging, installation and A advancement without synthesizing a target receipt.</summary>
internal static class ControlledPartitionMovementInstallationScenario
{
    private const int MaximumTransferFamilies = 55;

    internal static async Task<(PartitionMovePhaseResult Installed, PartitionMovePhaseResult Terminal,
        PartitionMoveSourceFenceRecord Fence, PartitionMovementCaptureHandle Handle,
        PartitionMovementPageResult[] Pages)> ExecuteAsync(
        ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, ServerRuntimeOptions sourceRuntime,
        ServerRuntimeOptions targetRuntime, PartitionMovementPeerAdmission sourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, string callerAddress, byte[] originalReceipt,
        long initialPosition, CancellationToken cancellationToken)
    {
        var capture = await ControlledPartitionMovementCaptureScenario.ExecuteAsync(source, target, corpus,
            sourceRuntime, sourceAdmission, callerAddress, originalReceipt, initialPosition, cancellationToken);
        await Assert.That(capture.Handle.PageCount <= MaximumTransferFamilies).IsTrue();
        await Assert.That(capture.Pages.Length).IsEqualTo(capture.Handle.PageCount);
        var fence = capture.Settlement.Fence
            ?? throw new InvalidOperationException("Actual original source fence is absent.");
        await ControlledPartitionMovementTargetStageFlow.ExecuteAsync(source, target, sourceRuntime,
            targetRuntime, sourceAdmission, targetAdmission, corpus, capture.Captured, fence,
            capture.Handle, capture.Pages, callerAddress, capture.Handle.ExpiresAt, cancellationToken);
        var terminal = await ControlledPartitionMovementTargetInstallFlow.ExecuteAsync(source, target,
            sourceRuntime, targetRuntime, sourceAdmission, targetAdmission, corpus, capture.Captured,
            fence, capture.Handle, callerAddress, capture.Handle.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementInstalledModelImage.AssertAsync(target, corpus, capture.Pages);
        var installed = await ControlledPartitionMovementInstalledAdvance.ExecuteAsync(source, sourceRuntime,
            sourceAdmission, corpus, capture.Captured, capture.Handle.Descriptor, terminal,
            capture.Handle.PageCount, callerAddress, capture.Handle.ExpiresAt, cancellationToken);
        return (installed, terminal, fence, capture.Handle, capture.Pages);
    }
}

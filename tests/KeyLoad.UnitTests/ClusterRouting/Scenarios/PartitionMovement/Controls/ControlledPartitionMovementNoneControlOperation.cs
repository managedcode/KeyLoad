using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises actual fatal apply, native repair, cold authority rebinding and complete target installation.</summary>
internal static class ControlledPartitionMovementNoneControlOperation
{
    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        ServerRuntimeOptions sourceRuntime, ServerRuntimeOptions targetRuntime,
        PartitionMovementPeerAdmission sourceAdmission, PartitionMovementPeerAdmission targetAdmission,
        string callerAddress, byte[] originalReceipt, ControlledPartitionMovementOutcomeAuthority originalAuthority,
        DateTimeOffset originalRecordedAt, long initialPosition, CancellationToken cancellationToken)
    {
        var capture = await ControlledPartitionMovementCaptureScenario.ExecuteAsync(source, target, corpus,
            sourceRuntime, sourceAdmission, callerAddress, originalReceipt, initialPosition, cancellationToken);
        var fence = capture.Settlement.Fence
            ?? throw new InvalidOperationException("The actual original source fence is absent.");
        await ControlledPartitionMovementTargetStageFlow.ExecuteAsync(source, target, sourceRuntime,
            targetRuntime, sourceAdmission, targetAdmission, corpus, capture.Captured, fence, capture.Handle,
            capture.Pages, callerAddress, capture.Handle.ExpiresAt, cancellationToken);
        var targetBefore = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var targetPosition = target.Store.Position;
        await ControlledPartitionMovementNoneControlFlow.ExecuteAsync(source, sourceRuntime, sourceAdmission,
            corpus, capture.Captured, fence, capture.Handle, callerAddress, capture.Handle.ExpiresAt,
            async repairedAdmission =>
            {
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store)
                    .SequenceEqual(targetBefore, StringComparer.Ordinal)).IsTrue();
                await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
                await ControlledPartitionMovementNoneControlHealthyInstallation.ExecuteAsync(source, target,
                    sourceRuntime, targetRuntime, repairedAdmission, targetAdmission, corpus,
                    capture.Captured, fence, capture.Handle, capture.Pages, callerAddress,
                    initialPosition, originalReceipt, originalAuthority, originalRecordedAt, cancellationToken);
            }, cancellationToken);
    }
}

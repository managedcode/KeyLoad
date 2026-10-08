using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Requires genuine healthy original-ID installation after repair, then complete cold model and original receipt evidence.</summary>
internal static class ControlledPartitionMovementNoneControlHealthyInstallation
{
    private const long FixedControlEntries = 22;
    private const long ControlEntriesPerPage = 4;
    private const long NativeCorruptionAndRepairCommits = 2;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ServerRuntimeOptions sourceRuntime,
        ServerRuntimeOptions targetRuntime, PartitionMovementPeerAdmission repairedSourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult captured, PartitionMoveSourceFenceRecord fence,
        PartitionMovementCaptureHandle handle, PartitionMovementPageResult[] pages, string callerAddress,
        long initialPosition, byte[] originalReceipt, ControlledPartitionMovementOutcomeAuthority originalAuthority,
        DateTimeOffset originalRecordedAt, CancellationToken cancellationToken)
    {
        var terminal = await ControlledPartitionMovementTargetInstallFlow.ExecuteAsync(source, target,
            sourceRuntime, targetRuntime, repairedSourceAdmission, targetAdmission, corpus, captured,
            fence, handle, callerAddress, handle.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementInstalledModelImage.AssertAsync(target, corpus, pages);
        var installed = await ControlledPartitionMovementInstalledAdvance.ExecuteAsync(source, sourceRuntime,
            repairedSourceAdmission, corpus, captured, handle.Descriptor, terminal, handle.PageCount,
            callerAddress, handle.ExpiresAt, cancellationToken);
        await Assert.That(installed.Control!.Phase).IsEqualTo(PartitionMovePhase.Installed);
        var sourceIndex = checked(FixedControlEntries + ControlEntriesPerPage * handle.PageCount);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(sourceIndex);
        await Assert.That(source.Store.Position).IsEqualTo(
            checked(initialPosition + sourceIndex + NativeCorruptionAndRepairCommits));
        await originalAuthority.RemainsControlOwnedAsync(source, target);
        var replay = source.Journal.Submit(ControlledPartitionMovementCorpus.SeedOperation(source.Database,
            originalRecordedAt), cancellationToken);
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(replay, originalReceipt);
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = target.Store.Position;
        source.Reopen();
        target.Reopen();
        await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, sourceImage,
            targetImage, sourcePosition, sourceIndex);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await ControlledPartitionMovementInstalledModelImage.AssertAsync(target, corpus, pages);
        await originalAuthority.RemainsControlOwnedAsync(source, target);
    }
}

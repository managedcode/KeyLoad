using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Executes genuine capture/install/finalize/publication/retirement and verifies both cold native owners.</summary>
internal static class ControlledPartitionMovementTerminalOperation
{
    private const long FixedControlEntries = 206;
    private const long ControlEntriesPerPage = 4;
    private const long FixedTargetEntries = 3;
    private const long TargetEntriesPerPage = 2;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        ServerRuntimeOptions sourceRuntime, ServerRuntimeOptions targetRuntime,
        PartitionMovementPeerAdmission sourceAdmission, PartitionMovementPeerAdmission targetAdmission,
        string callerAddress, byte[] originalReceipt, ControlledPartitionMovementOutcomeAuthority originalAuthority,
        DateTimeOffset originalRecordedAt, long initialPosition, DateTimeOffset wholeExpiresAt, TimeSpan issuedExpiryWindow, CancellationToken cancellationToken)
    {
        var installed = await ControlledPartitionMovementInstallationScenario.ExecuteAsync(source, target,
            corpus, sourceRuntime, targetRuntime, sourceAdmission, targetAdmission, callerAddress,
            originalReceipt, initialPosition, cancellationToken);
        var finalized = await ControlledPartitionMovementFinalizeFlow.ExecuteAsync(source, sourceRuntime,
            sourceAdmission, corpus, installed.Installed, callerAddress, installed.Handle.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementTargetPublishFlow.ExecuteAsync(source, target, sourceRuntime,
            targetRuntime, sourceAdmission, targetAdmission, corpus, finalized,
            installed.Handle.Descriptor.Resources, callerAddress, installed.Handle.ExpiresAt, cancellationToken);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var targetPosition = target.Store.Position;
        var retired = await ControlledPartitionMovementRetireFlow.ExecuteAsync(source, target, sourceRuntime,
            sourceAdmission, corpus, finalized, callerAddress, wholeExpiresAt, issuedExpiryWindow, cancellationToken);
        await ControlledPartitionMovementRetirementComplete.ExecuteAsync(source, sourceRuntime, sourceAdmission,
            corpus, finalized, retired.Terminal, retired.GrantId, retired.OriginalBody,
            callerAddress, wholeExpiresAt, cancellationToken);
        var sourceIndex = checked(FixedControlEntries + ControlEntriesPerPage * installed.Handle.PageCount);
        var targetIndex = checked(FixedTargetEntries + TargetEntriesPerPage * installed.Handle.PageCount);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(sourceIndex);
        await Assert.That(source.Store.Position).IsEqualTo(checked(initialPosition + sourceIndex));
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store)
            .SequenceEqual(targetImage, StringComparer.Ordinal)).IsTrue();
        await ControlledPartitionMovementRetiredSourceAssertions.AssertAsync(source, target,
            originalAuthority, originalReceipt, originalRecordedAt, cancellationToken);
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var sourcePosition = source.Store.Position;
        source.Reopen();
        target.Reopen();
        await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, sourceImage,
            targetImage, sourcePosition, sourceIndex);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await ControlledPartitionMovementInstalledModelImage.AssertAsync(target, corpus, installed.Pages);
        await ControlledPartitionMovementRetiredSourceAssertions.AssertAsync(source, target,
            originalAuthority, originalReceipt, originalRecordedAt, cancellationToken);
    }
}

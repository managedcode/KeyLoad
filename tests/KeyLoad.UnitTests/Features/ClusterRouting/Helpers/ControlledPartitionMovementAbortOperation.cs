using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Preserves the complete source model and original receipts through real abort and both cold native owners.</summary>
internal static class ControlledPartitionMovementAbortOperation
{
    private const long FinalSourceIndex = 145;
    private const long FinalTargetIndex = 61;
    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        ServerRuntimeOptions sourceRuntime, ServerRuntimeOptions targetRuntime,
        PartitionMovementPeerAdmission sourceAdmission, PartitionMovementPeerAdmission targetAdmission,
        string callerAddress, byte[] originalReceipt, ControlledPartitionMovementOutcomeAuthority originalAuthority,
        BlobMetadata originalBlob, DateTimeOffset originalRecordedAt, long initialPosition, CancellationToken cancellationToken)
    {
        var prepared = await ControlledPartitionMovementPrepareFlow.ExecuteAsync(source, target, corpus,
            sourceRuntime, sourceAdmission, callerAddress, cancellationToken);
        var fence = await ControlledPartitionMovementGrantFlow.ExecuteAsync(source, sourceRuntime,
            sourceAdmission, corpus, prepared.Prepared, callerAddress, prepared.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementFenceStateAssertions.RetainedAsync(source, originalReceipt,
            initialPosition, cancellationToken);
        var accepted = await ControlledPartitionMovementFenceSettlementFlow.ExecuteAsync(source, sourceRuntime,
            sourceAdmission, corpus, prepared.Prepared, fence.Authorization, fence.Fence, callerAddress,
            prepared.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementActiveAbort.ExecuteAsync(source, target, sourceRuntime, targetRuntime,
            sourceAdmission, targetAdmission, corpus, accepted, callerAddress, prepared.ExpiresAt,
            cancellationToken);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(FinalSourceIndex);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(FinalTargetIndex);
        await Assert.That(source.Store.Position).IsEqualTo(checked(initialPosition + FinalSourceIndex));
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(source.Journal.Submit(
            ControlledPartitionMovementCorpus.SeedOperation(source.Database, originalRecordedAt), cancellationToken), originalReceipt);
        await ControlledPartitionMovementModelAssertions.ReadAsync(source, originalRecordedAt, source.Store.Position, cancellationToken);
        await ControlledPartitionMovementBlobAssertions.ReadAsync(source, originalBlob, cancellationToken);
        await originalAuthority.RemainsControlOwnedAsync(source, target);
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var sourceIndex = source.Journal.Log.State.LastIndex;
        var targetPosition = target.Store.Position;
        var targetIndex = target.Journal.Log.State.LastIndex;
        source.Reopen();
        target.Reopen();
        await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, sourceImage, targetImage,
            sourcePosition, sourceIndex);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(source.Journal.Submit(
            ControlledPartitionMovementCorpus.SeedOperation(source.Database, originalRecordedAt), cancellationToken), originalReceipt);
        await ControlledPartitionMovementModelAssertions.ReadAsync(source, originalRecordedAt, sourcePosition, cancellationToken);
        await ControlledPartitionMovementBlobAssertions.ReadAsync(source, originalBlob, cancellationToken);
        await originalAuthority.RemainsControlOwnedAsync(source, target);
        await ControlledPartitionMovementAbortHealthy.ExecuteAsync(source, corpus, cancellationToken);
        await originalAuthority.RemainsControlOwnedAsync(source, target);
    }
}

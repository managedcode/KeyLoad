using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Cold reopens both original owners after genuine healthy settlement and checks full immutable inputs.</summary>
internal static class ControlledPartitionMovementProcessColdHealthy
{
    private const long HealthyIndex = 15;
    private const long HealthyPosition = 16;

    internal static async Task ExecuteAsync(string sourceRoot, string targetRoot,
        ControlledPartitionMovementProcessOwners owners, ControlledPartitionMovementProcessPrepared prepared,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new ControlledPartitionMovementNativeNode(sourceRoot, owners.Control.Owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var target = new ControlledPartitionMovementNativeNode(targetRoot, owners.Destination.Owner);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(source.Store.Position).IsEqualTo(HealthyPosition);
                    await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(HealthyIndex);
                    var image = ControlledPartitionMovementProcessRawImage.Bytes(source.Store);
                    var replay = source.Journal.Submit(prepared.OriginalSeed, cancellationToken);
                    await ControlledPartitionMovementProcessReceiptAssertions.ReplayAsync(replay, prepared.OriginalReceipt);
                    await ControlledPartitionMovementProcessModelAssertions.ReadAsync(source, prepared.RecordedAt,
                        HealthyPosition, cancellationToken);
                    var blob = new BlobMetadata(ControlledPartitionMovementProcessLiteralCorpus.Blob,
                        HealthyRevision, ControlledPartitionMovementProcessLiteralCorpus.BlobUploadId,
                        ControlledPartitionMovementProcessLiteralCorpus.BlobBytes().Length, BlobParts,
                        ControlledPartitionMovementProcessLiteralCorpus.BlobIntegrityHash(owners.Control.Owner.Incarnation), new RowAccess(), prepared.BlobEvaluatedAt, false);
                    await ControlledPartitionMovementProcessBlobAssertions.ReadAsync(source, blob, cancellationToken);
                    await ControlledPartitionMovementProcessBlobAssertions.ReplayAsync(source, prepared.OriginalBlob,
                        prepared.BlobEvaluatedAt, prepared.OriginalBlobOutcome, cancellationToken);
                    await Assert.That(ControlledPartitionMovementProcessRawImage.Bytes(source.Store).SequenceEqual(image)).IsTrue();
                    await Assert.That(source.Store.Position).IsEqualTo(HealthyPosition);
                    await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(HealthyIndex);
                    await Assert.That(ControlledPartitionMovementProcessRawImage.Bytes(target.Store)
                        .SequenceEqual(prepared.UntouchedTargetImage)).IsTrue();
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private const long HealthyRevision = 1;
    private const int BlobParts = 1;
}

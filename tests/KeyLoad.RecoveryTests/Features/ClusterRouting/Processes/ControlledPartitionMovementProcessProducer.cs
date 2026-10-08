using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Joins both genuine parent native owners before transferring the original phase to a child.</summary>
internal static class ControlledPartitionMovementProcessProducer
{
    private const long SeedIndex = 6;
    private const long BlobIndex = 10;
    private const long InitialEpoch = 1;
    private const long BeforeIndex = 12;
    private const long BeforePosition = 13;

    internal static async Task<ControlledPartitionMovementProcessPrepared> PrepareAsync(string sourceRoot,
        string targetRoot, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementProcessOwners owners, CommitStage cut, CancellationToken cancellationToken)
    {
        ControlledPartitionMovementProcessPrepared? original = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new ControlledPartitionMovementNativeNode(sourceRoot, owners.Control.Owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var target = new ControlledPartitionMovementNativeNode(targetRoot, owners.Destination.Owner);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    original = await ControlledPartitionMovementProcessPreparation.PrepareAsync(source, target,
                        listeners, owners, cut, cancellationToken);
                    await Assert.That(original.Input.BeforePosition).IsEqualTo(BeforePosition);
                    await Assert.That(original.Input.BeforeReplicaIndex).IsEqualTo(BeforeIndex);
                    await ControlledPartitionMovementProcessModelAssertions.ReadAsync(source, original.RecordedAt,
                        BeforePosition, cancellationToken);
                    await ControlledPartitionMovementProcessReceiptAssertions.SeedAsync(
                        source.Journal.Submit(original.OriginalSeed, cancellationToken),
                        new(owners.Control.Owner.Incarnation,
                            ControlledPartitionMovementProcessLiteralCorpus.Partition.AtomicPartitionId, SeedIndex, InitialEpoch),
                        DurabilityProfile.ProcessDurable);
                    var blob = await ControlledPartitionMovementProcessBlobAssertions.PublishedAsync(
                        original.OriginalBlobOutcome, original.OriginalBlob, original.BlobEvaluatedAt,
                        new(owners.Control.Owner.Incarnation,
                            ControlledPartitionMovementProcessLiteralCorpus.Partition.AtomicPartitionId, BlobIndex, InitialEpoch),
                        DurabilityProfile.ProcessDurable);
                    await ControlledPartitionMovementProcessBlobAssertions.ReadAsync(source, blob, cancellationToken);
                    await ControlledPartitionMovementProcessFiles.WriteAsync(sourceRoot,
                        ControlledPartitionMovementProcessProtocol.InputFile, original.Input, cancellationToken);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return original ?? throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid);
    }
}

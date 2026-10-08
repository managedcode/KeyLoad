using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Checks independently expected recovered values after children exit, preserving first recovery ownership.</summary>
internal sealed class ControlledPartitionMovementProcessReadback(
    string sourceRoot, string targetRoot, ControlledPartitionMovementProcessOwners owners,
    ControlledPartitionMovementProcessPrepared prepared)
{
    private const long RecoveredIndex = 13;
    private const long RecoveredPosition = 14;
    private const long SeedIndex = 6;
    private const long InitialEpoch = 1;
    private const long BlobRevision = 1;
    private const int BlobParts = 1;
    private string[]? recoveredImage;

    internal async Task ObserveAsync(string mode, CancellationToken cancellationToken)
    {
        if (mode is ControlledPartitionMovementProcessProtocol.Prepare
            or ControlledPartitionMovementProcessProtocol.Fault)
        { return; }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new ControlledPartitionMovementNativeNode(sourceRoot, owners.Control.Owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var target = new ControlledPartitionMovementNativeNode(targetRoot, owners.Destination.Owner);
                await ServerFailureObserver.ObserveAsync(() => CompleteAsync(source, target, mode, cancellationToken), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task CompleteAsync(ControlledPartitionMovementNativeNode source,
        ControlledPartitionMovementNativeNode target, string mode, CancellationToken cancellationToken)
    {
        var file = mode == ControlledPartitionMovementProcessProtocol.Recover
            ? ControlledPartitionMovementProcessProtocol.RecoveredFile : ControlledPartitionMovementProcessProtocol.VerifiedFile;
        var result = await ControlledPartitionMovementProcessFiles.ReadAsync<OperationResult>(sourceRoot, file, cancellationToken);
        await ControlledPartitionMovementProcessPhaseAssertions.CompleteAsync(result, owners);
        await Assert.That(source.Store.Position).IsEqualTo(RecoveredPosition);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(RecoveredIndex);
        await Assert.That(source.Journal.Log.State.CommittedIndex).IsEqualTo(RecoveredIndex);
        var entry = source.Journal.Log.ReadEntry(RecoveredIndex)
            ?? throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid);
        var originalEntry = entry.Operation
            ?? throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid);
        await Assert.That(JsonDefaults.Serialize(originalEntry)
            .SequenceEqual(JsonDefaults.Serialize(prepared.Input.OriginalOperation))).IsTrue();
        await Assert.That(originalEntry.NativePayload.Span
            .SequenceEqual(prepared.Input.OriginalOperation.NativePayload.Span)).IsTrue();
        var image = ControlledPartitionMovementProcessRawImage.Bytes(source.Store);
        var replay = source.Journal.Submit(prepared.OriginalSeed, cancellationToken);
        var literalReceipt = await ControlledPartitionMovementProcessReceiptAssertions.SeedAsync(replay,
            new(owners.Control.Owner.Incarnation, ControlledPartitionMovementProcessLiteralCorpus.Partition.AtomicPartitionId,
                SeedIndex, InitialEpoch), DurabilityProfile.ProcessDurable);
        await Assert.That(literalReceipt.SequenceEqual(prepared.OriginalReceipt)).IsTrue();
        await ControlledPartitionMovementProcessModelAssertions.ReadAsync(source, prepared.RecordedAt,
            RecoveredPosition, cancellationToken);
        await ControlledPartitionMovementProcessBlobAssertions.ReadAsync(source, ExpectedBlob(), cancellationToken);
        await ControlledPartitionMovementProcessBlobAssertions.ReplayAsync(source, prepared.OriginalBlob,
            prepared.BlobEvaluatedAt, prepared.OriginalBlobOutcome, cancellationToken);
        await Assert.That(ControlledPartitionMovementProcessRawImage.Bytes(source.Store).SequenceEqual(image)).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(RecoveredPosition);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(RecoveredIndex);
        await Assert.That(ControlledPartitionMovementProcessRawImage.Bytes(target.Store)
            .SequenceEqual(prepared.UntouchedTargetImage)).IsTrue();
        if (recoveredImage is not null)
        { await Assert.That(image.SequenceEqual(recoveredImage)).IsTrue(); }
        recoveredImage ??= image;
    }

    private BlobMetadata ExpectedBlob() => new(ControlledPartitionMovementProcessLiteralCorpus.Blob,
        BlobRevision, ControlledPartitionMovementProcessLiteralCorpus.BlobUploadId, ControlledPartitionMovementProcessLiteralCorpus.BlobBytes().Length,
        BlobParts, ControlledPartitionMovementProcessLiteralCorpus.BlobIntegrityHash(owners.Control.Owner.Incarnation), new RowAccess(), prepared.BlobEvaluatedAt, false);
}

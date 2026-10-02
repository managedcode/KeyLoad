using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-004: real private-transfer metadata failures preserve the canonical cut and permit safe retry.</summary>
internal sealed class ReplicaSnapshotMetadataRecoveryTests
{
    private static readonly byte[] TruncatedManifest = "{"u8.ToArray();

    /// <summary>A foreign-incarnation descriptor is rejected before private files or canonical state are changed.</summary>
    [Test]
    public async Task ForeignIncarnationSnapshotIsRejectedWithoutEffectsAndValidDescriptorCanRetry()
    {
        using var trial = new ReplicaSnapshotTrial();
        var receiver = trial.Receiver();
        var foreign = trial.Image with { Incarnation = Guid.NewGuid() };
        var error = Assert.ThrowsExactly<KeyLoadException>(() => receiver.Begin(foreign));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(receiver.Current).IsNull();
        await Assert.That(trial.TargetLog.State.CommittedIndex).IsEqualTo(0);
        await Assert.That(trial.Target.Identity.ReadGeneration).IsEqualTo(0);
        await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey))))
            .IsEqualTo(ReplicaSnapshotTrial.OldValue);
        var directory = Path.GetDirectoryName(trial.TargetImagePath(trial.Image))!;
        await Assert.That(File.Exists(Path.Combine(directory, ReplicaProtocol.IncomingManifest))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(directory, ReplicaProtocol.IncomingImage))).IsFalse();

        await Assert.That(receiver.Begin(trial.Image)).IsEqualTo(0);
        trial.Transfer(receiver);
        await Assert.That(receiver.Complete(trial.Image.TransferId)).IsEqualTo(trial.Image);
        await Assert.That(trial.Target.Identity.ReadGeneration).IsEqualTo(1);
        await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey))))
            .IsEqualTo(ReplicaSnapshotTrial.NewValue);
    }

    /// <summary>A truncated private manifest is reclaimed without discarding canonical data, then a valid image installs.</summary>
    [Test]
    public async Task TruncatedPrivateManifestIsDiscardedAndVerifiedTransferCanRetry()
    {
        using var trial = new ReplicaSnapshotTrial();
        var receiver = trial.Receiver();
        await Assert.That(receiver.Begin(trial.Image)).IsEqualTo(0);
        var directory = Path.GetDirectoryName(trial.TargetImagePath(trial.Image))!;
        await File.WriteAllBytesAsync(Path.Combine(directory, ReplicaProtocol.IncomingManifest), TruncatedManifest,
            TestContext.Current!.Execution.CancellationToken);

        receiver.Recover();

        await Assert.That(receiver.Current).IsNull();
        await Assert.That(trial.TargetLog.State.CommittedIndex).IsEqualTo(0);
        await Assert.That(trial.Target.Identity.ReadGeneration).IsEqualTo(0);
        await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey))))
            .IsEqualTo(ReplicaSnapshotTrial.OldValue);
        await Assert.That(File.Exists(Path.Combine(directory, ReplicaProtocol.IncomingManifest))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(directory, ReplicaProtocol.IncomingImage))).IsFalse();

        await Assert.That(receiver.Begin(trial.Image)).IsEqualTo(0);
        trial.Transfer(receiver);
        await Assert.That(receiver.Complete(trial.Image.TransferId)).IsEqualTo(trial.Image);
        await Assert.That(trial.Target.Identity.ReadGeneration).IsEqualTo(1);
        await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey))))
            .IsEqualTo(ReplicaSnapshotTrial.NewValue);
    }
}

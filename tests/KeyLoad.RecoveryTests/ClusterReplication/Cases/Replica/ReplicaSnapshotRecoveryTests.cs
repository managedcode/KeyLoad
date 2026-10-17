using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-004: rejection and interruption regressions for real canonical snapshot transfers.</summary>
internal sealed class ReplicaSnapshotRecoveryTests
{
    /// <summary>AC-REP-004: published-image corruption and unsafe filenames fail closed.</summary>
    [Test]
    public async Task CorruptPublishedImageFailsClosedAndUnsafeTransferBasenameIsRejected()
    {
        using var trial = new ReplicaSnapshotTrial();
        File.WriteAllBytes(trial.ImagePath, [1]);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(trial.Sender.Recover).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(trial.Sender.Current).IsEqualTo(trial.Image);
        var receiver = trial.Receiver();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.Begin(trial.Image with { FileName = ReplicaSnapshotTrial.UnsafePath })).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(trial.TargetLog.State.Snapshot).IsNull();
    }

    /// <summary>AC-REP-004: recovery finishes verified installs and remains idempotent.</summary>
    [Test]
    [Arguments(ReplicaCrashBoundary.SnapshotVerified)]
    [Arguments(ReplicaCrashBoundary.SnapshotInstalled)]
    public async Task ResetFinishesVerifiedIncomingImageAndDoesNotReinstallAfterPointerPublication(ReplicaCrashBoundary boundary)
    {
        using var trial = new ReplicaSnapshotTrial();
        var receiver = trial.Receiver(stage =>
        {
            if (stage == boundary)
            {
                throw new IOException(ReplicaSnapshotTrial.Interrupted);
            }
        });
        trial.Transfer(receiver);
        Assert.ThrowsExactly<IOException>(() => receiver.Complete(trial.Image.TransferId));
        await Assert.That(trial.TargetLog.State.Snapshot).IsNull();
        if (boundary == ReplicaCrashBoundary.SnapshotVerified)
        {
            await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey)))).IsEqualTo(ReplicaSnapshotTrial.OldValue);
        }
        receiver = trial.Receiver();
        receiver.ResetIncoming();
        await Assert.That(trial.TargetLog.State.Snapshot).IsEqualTo(trial.Image);
        await Assert.That(trial.Target.Identity.ReadGeneration).IsEqualTo(1);
        await Assert.That(File.Exists(trial.TargetImagePath(trial.Image))).IsTrue();
        receiver.Recover();
        await Assert.That(trial.Target.Identity.ReadGeneration).IsEqualTo(1);
    }

    /// <summary>AC-REP-004: rejected complete images preserve canonical data and allow a safe retry.</summary>
    [Test]
    public async Task CorruptCompleteImagePreservesLiveStateAndAllowsSameDescriptorRetry()
    {
        using var trial = new ReplicaSnapshotTrial();
        var receiver = trial.Receiver();
        receiver.Begin(trial.Image);
        var bytes = trial.Sender.ReadChunk(trial.Image.TransferId, 0, trial.Configuration.SnapshotChunkBytes);
        bytes[^1] ^= 1;
        receiver.Append(trial.Image.TransferId, 0, bytes);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.Complete(trial.Image.TransferId)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey)))).IsEqualTo(ReplicaSnapshotTrial.OldValue);
        await Assert.That(trial.TargetLog.State.Snapshot).IsNull();
        trial.Transfer(receiver);
        receiver.Complete(trial.Image.TransferId);
        await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey)))).IsEqualTo(ReplicaSnapshotTrial.NewValue);
    }

    /// <summary>AC-REP-004: replay and transfer identity cannot rewrite acknowledged chunks.</summary>
    [Test]
    public async Task ChunkReplayMustMatchAndDifferentActiveTransferCannotReplaceAcknowledgedUpload()
    {
        using var trial = new ReplicaSnapshotTrial();
        var receiver = trial.Receiver();
        receiver.Begin(trial.Image);
        var bytes = trial.Sender.ReadChunk(trial.Image.TransferId, 0, 32);
        receiver.Append(trial.Image.TransferId, 0, bytes);
        await Assert.That(receiver.Append(trial.Image.TransferId, 0, bytes)).IsEqualTo(bytes.LongLength);
        bytes[^1] ^= 1;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.Append(trial.Image.TransferId, 0, bytes)).Code).IsEqualTo(ErrorCode.Conflict);
        var otherId = Guid.NewGuid();
        var other = trial.Image with
        {
            TransferId = otherId,
            FileName = otherId.ToString(ReplicaSnapshotTrial.GuidFormat) + ReplicaProtocol.SnapshotExtension
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.Begin(other)).Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(receiver.Begin(trial.Image)).IsEqualTo(bytes.LongLength);
    }

    /// <summary>AC-REP-004: stale pending images cannot erase a newer canonical cut.</summary>
    [Test]
    public async Task StalePendingSnapshotCannotEraseNewerCanonicalCommittedState()
    {
        using var trial = new ReplicaSnapshotTrial();
        var receiver = trial.Receiver();
        trial.Transfer(receiver);
        trial.Target.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode(ReplicaSnapshotTrial.SystemKey, ReplicaSnapshotTrial.AppliedKey), 2L); return true; });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(receiver.ResetIncoming).Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(trial.TargetLog.State.Snapshot).IsNull();
        await Assert.That(trial.Target.Read(view => NativeSerialization.Deserialize<long>(view.ReadOwnedValue(KeyCodec.Encode(ReplicaSnapshotTrial.SystemKey, ReplicaSnapshotTrial.AppliedKey))!))).IsEqualTo(2);
        await Assert.That(trial.Target.Identity.ReadGeneration).IsEqualTo(0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.Begin(trial.Image)).Code).IsEqualTo(ErrorCode.Conflict);
    }

    /// <summary>AC-REP-004: leader-fenced reset abandons an old prefix while preserving the published canonical cut.</summary>
    [Test]
    public async Task ResetAbandonsOldLeaderPrefixAndAllowsDifferentUploadWithoutDiscardingPublishedImage()
    {
        using var trial = new ReplicaSnapshotTrial();
        var receiver = trial.Receiver();
        trial.Transfer(receiver);
        receiver.Complete(trial.Image.TransferId);
        var oldPending = trial.Sender.Create(1, 1);
        receiver.Begin(oldPending);
        receiver.Append(oldPending.TransferId, 0, trial.Sender.ReadChunk(oldPending.TransferId, 0, 32));
        var replacement = trial.Sender.Create(1, 1);
        receiver = trial.Receiver();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.Begin(replacement)).Code).IsEqualTo(ErrorCode.Conflict);
        receiver.ResetIncoming();
        await Assert.That(receiver.Current).IsEqualTo(trial.Image);
        await Assert.That(File.Exists(trial.TargetImagePath(trial.Image))).IsTrue();
        await Assert.That(trial.TargetLog.State.CommittedIndex).IsEqualTo(1);
        await Assert.That(trial.Target.Read(view => NativeSerialization.Deserialize<long>(view.ReadOwnedValue(KeyCodec.Encode(ReplicaSnapshotTrial.SystemKey, ReplicaSnapshotTrial.AppliedKey))!))).IsEqualTo(1);
        await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey)))).IsEqualTo(ReplicaSnapshotTrial.NewValue);
        await Assert.That(receiver.Begin(replacement)).IsEqualTo(0);
        trial.Transfer(receiver, replacement);
        receiver.Complete(replacement.TransferId);
        await Assert.That(receiver.Current).IsEqualTo(replacement);
        await Assert.That(File.Exists(trial.TargetImagePath(trial.Image))).IsTrue();
        await Assert.That(trial.Target.Identity.ReadGeneration).IsEqualTo(1);
    }
}

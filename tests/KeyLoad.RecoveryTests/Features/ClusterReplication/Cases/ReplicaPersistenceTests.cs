using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-002/004: real-store metadata and bounded snapshot regressions, qualified only by CI execution.</summary>
internal sealed class ReplicaPersistenceTests
{
    /// <summary>AC-REP-002: durable metadata reopens and committed entries cannot be replaced.</summary>
    [Test]
    public async Task AcknowledgedVoteAndCommittedPrefixSurviveReopenAndRejectReplacement()
    {
        var directory = NewDirectory();
        var configuration = Configuration(directory);
        try
        {
            using (var store = Store(directory, configuration.Incarnation))
            using (var log = new DurableReplicaLog(store, RecoveryExecutionOptions.Configuration(configuration)))
            {
                log.SaveTermAndVote(2, VoterA);
                log.Append([new(1, 2, null), new(2, 2, null), new(3, 2, null)]);
                log.Commit(2);
            }
            using var reopened = Store(directory, configuration.Incarnation);
            using var recovered = new DurableReplicaLog(reopened, RecoveryExecutionOptions.Configuration(configuration));
            await Assert.That(recovered.State.VotedFor).IsEqualTo(VoterA);
            await Assert.That(recovered.State.CommittedIndex).IsEqualTo(2);
            recovered.SaveTermAndVote(3, null);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => recovered.Append([new(2, 3, null)])).Code).IsEqualTo(ErrorCode.Conflict);
            recovered.Append([new(3, 3, null)]);
            await Assert.That(recovered.TermAt(2)).IsEqualTo(2);
            await Assert.That(recovered.TermAt(3)).IsEqualTo(3);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>AC-REP-002: one vote per term remains sticky and voter identities are fenced.</summary>
    [Test]
    public async Task SameTermCannotForgetItsVoteOrVoteForAnUnknownVoter()
    {
        var directory = NewDirectory();
        var configuration = Configuration(directory);
        try
        {
            using var store = Store(directory, configuration.Incarnation);
            using var log = new DurableReplicaLog(store, RecoveryExecutionOptions.Configuration(configuration));
            log.SaveTermAndVote(1, VoterA);
            log.SaveTermAndVote(1, null);
            await Assert.That(log.State.VotedFor).IsEqualTo(VoterA);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => log.SaveTermAndVote(1, VoterB)).Code).IsEqualTo(ErrorCode.Conflict);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => log.SaveTermAndVote(2, UnknownVoter)).Code).IsEqualTo(ErrorCode.Conflict);
            log.SaveTermAndVote(2, null);
            await Assert.That(log.State.VotedFor).IsNull();
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>AC-REP-002: logical truncation hides obsolete suffix keys and denies gaps.</summary>
    [Test]
    public async Task ReplacingUncommittedSuffixHidesOldKeysAndRejectsGaps()
    {
        var directory = NewDirectory();
        var configuration = Configuration(directory);
        try
        {
            using var store = Store(directory, configuration.Incarnation);
            using var log = new DurableReplicaLog(store, RecoveryExecutionOptions.Configuration(configuration));
            log.SaveTermAndVote(1, null);
            log.Append([new(1, 1, null), new(2, 1, null), new(3, 1, null)]);
            log.Commit(1);
            log.SaveTermAndVote(2, null);
            log.Append([new(2, 2, null)]);
            await Assert.That(log.State.LastIndex).IsEqualTo(2);
            await Assert.That(log.ReadEntry(3)).IsNull();
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => log.Append([new(4, 2, null)])).Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(store.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(3)))).IsNotNull();
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>AC-REP-002: an eight MiB command is accepted and undersized reads fail explicitly.</summary>
    [Test]
    [Arguments(ReplicaMaximumPayload.Quotes)]
    [Arguments(ReplicaMaximumPayload.Unicode)]
    public async Task SerializedReadBudgetAcceptsMaximumCommandAndRejectsAnOversizeSingleEntry(string token)
    {
        var directory = NewDirectory();
        var configuration = Configuration(directory);
        try
        {
            using var store = Store(directory, configuration.Incarnation);
            using var canonical = Store(Path.Combine(directory, CanonicalDirectory), configuration.Incarnation);
            var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
            using var log = new DurableReplicaLog(store, RecoveryExecutionOptions.Configuration(configuration), canonicalDatabase: database);
            log.SaveTermAndVote(1, null);
            var payload = ReplicaMaximumPayload.Create(token, MaximumCommandBytes);
            using var parsed = JsonDocument.Parse(payload);
            await Assert.That(parsed.RootElement.ValueKind).IsEqualTo(JsonValueKind.Object);
            await Assert.That(Encoding.UTF8.GetByteCount(payload)).IsEqualTo(MaximumCommandBytes);
            var operation = database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.Batch, VoterA, DateTimeOffset.UnixEpoch, payload));
            var encoded = ReplicaProtocolCodec.Serialize(operation);
            var decoded = ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(encoded);
            await Assert.That(decoded.PayloadJson).IsEqualTo(payload);
            await Assert.That(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(decoded.PayloadJson)))).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(ReplicaMaximumPayload.LegacyJson(operation))).Code).IsEqualTo(ErrorCode.FormatUnsupported);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.DeserializeStored<ReplicatedOperation>(ReplicaMaximumPayload.Malformed(operation, ReplicaMaximumPayload.UnknownField), configuration.MaxAppendEntries)).Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.DeserializeStored<ReplicatedOperation>(ReplicaMaximumPayload.Malformed(operation, ReplicaMaximumPayload.DuplicateField), configuration.MaxAppendEntries)).Code).IsEqualTo(ErrorCode.Corruption);
            log.Append([new(1, 1, operation)]);
            log.Commit(1);
            log.Append([new(1, 1, decoded)]);
            await ReplicaProcessAssertions.OperationAsync(database, log.Read(1, 1, configuration.MaxAppendBytes)[0].Operation, operation);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => log.Read(1, 1, MaximumCommandBytes)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>AC-REP-002: checksummed records still undergo semantic validation on reopen.</summary>
    [Test]
    public async Task ReopenRejectsChecksummedButSemanticallyCorruptVisibleEntry()
    {
        var directory = NewDirectory();
        var configuration = Configuration(directory);
        try
        {
            using var store = Store(directory, configuration.Incarnation);
            using (var log = new DurableReplicaLog(store, RecoveryExecutionOptions.Configuration(configuration)))
            {
                log.SaveTermAndVote(1, null);
                log.Append([new(1, 1, null)]);
            }
            store.Commit((tx, _) => { tx.Put(ReplicaProtocol.EntryStorageKey(1), ReplicaProtocolCodec.Serialize(new ReplicaEntry(2, 1, null))); return true; });
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            {
                using var rejected = new DurableReplicaLog(store, RecoveryExecutionOptions.Configuration(configuration));
            }).Code).IsEqualTo(ErrorCode.Corruption);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>AC-REP-004: durable chunk offsets resume and install the verified canonical cut.</summary>
    [Test]
    public async Task ContiguousChunksResumeAndVerifiedSnapshotInstallsAtExactCut()
    {
        using var trial = new ReplicaSnapshotTrial();
        var receiver = trial.Receiver();
        await Assert.That(receiver.Begin(trial.Image)).IsEqualTo(0);
        var first = trial.Sender.ReadChunk(trial.Image.TransferId, 0, 32);
        await Assert.That(receiver.Append(trial.Image.TransferId, 0, first)).IsEqualTo(first.LongLength);
        receiver = trial.Receiver();
        receiver.Recover();
        await Assert.That(receiver.Begin(trial.Image)).IsEqualTo(first.LongLength);
        trial.Transfer(receiver);
        receiver.Complete(trial.Image.TransferId);
        await Assert.That(trial.TargetLog.State.Snapshot).IsEqualTo(trial.Image);
        await Assert.That(trial.Target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ReplicaSnapshotTrial.ValueKey)))).IsEqualTo(ReplicaSnapshotTrial.NewValue);
        await Assert.That(trial.TargetLog.TermAt(1)).IsEqualTo(1);
    }

    private const string VoterA = "a";
    private const string VoterB = "b";
    private const string VoterC = "c";
    private const string Prefix = "keyload-replica-persistence-";
    private const string CanonicalDirectory = "canonical";
    private const string UnknownVoter = "unknown";
    private const int MaximumCommandBytes = 8_388_608;
    private static string NewDirectory() => ReplicaFixturePaths.NewDirectory(Prefix);
    private static ReplicaConfiguration Configuration(string directory) => new(VoterA, [VoterA, VoterB, VoterC], directory, Guid.NewGuid());
    private static ZoneTreeStore Store(string directory, Guid incarnation) => new(new(directory) { Incarnation = incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
}

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
            { throw new IOException(ReplicaSnapshotTrial.Interrupted); }
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
        var other = trial.Image with { TransferId = otherId, FileName = otherId.ToString(ReplicaSnapshotTrial.GuidFormat) + ReplicaProtocol.SnapshotExtension };
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

internal sealed class ReplicaSnapshotTrial : IDisposable
{
    internal const string Interrupted = "interrupted-at-replica-boundary";
    internal const string ValueKey = "value";
    internal const string OldValue = "old-value";
    internal const string NewValue = "new-value";
    internal const string SystemKey = "system";
    internal const string AppliedKey = "last-applied";
    internal const string GuidFormat = "N";
    internal const string UnsafePath = "../foreign.snapshot";
    private const string Prefix = "keyload-replica-transfer-";
    private const string SourceDirectory = "source";
    private const string TargetDirectory = "target";
    private const string SourceLogDirectory = "source-log";
    private const string TargetLogDirectory = "target-log";
    private const string VoterA = "a";
    private const string VoterB = "b";
    private const string VoterC = "c";
    private readonly string directory = ReplicaFixturePaths.NewDirectory(Prefix);
    private readonly ZoneTreeStore source;
    private readonly ZoneTreeStore sourceLogStore;
    private readonly ZoneTreeStore targetLogStore;
    private readonly DurableReplicaLog sourceLog;
    internal ReplicaConfiguration Configuration { get; }
    internal ZoneTreeStore Target { get; }
    internal DurableReplicaLog TargetLog { get; }
    internal ReplicaSnapshotStore Sender { get; }
    internal ReplicaSnapshot Image { get; }
    internal string ImagePath => Path.Combine(directory, SourceDirectory, ReplicaProtocol.SnapshotDirectory, Image.FileName);
    internal string TargetImagePath(ReplicaSnapshot image) => Path.Combine(directory, TargetDirectory, ReplicaProtocol.SnapshotDirectory, image.FileName);

    internal ReplicaSnapshotTrial()
    {
        Configuration = new(VoterA, [VoterA, VoterB, VoterC], directory, Guid.NewGuid());
        source = Store(SourceDirectory);
        Target = Store(TargetDirectory);
        sourceLogStore = Store(SourceLogDirectory);
        targetLogStore = Store(TargetLogDirectory);
        sourceLog = new(sourceLogStore, RecoveryExecutionOptions.Configuration(Configuration));
        TargetLog = new(targetLogStore, RecoveryExecutionOptions.Configuration(Configuration));
        sourceLog.SaveTermAndVote(1, null);
        sourceLog.Append([new(1, 1, null)]);
        sourceLog.Commit(1);
        source.Commit((tx, _) =>
        {
            tx.PutRecord(KeyCodec.Encode(SystemKey, AppliedKey), 1L);
            tx.PutRecord(KeyCodec.Encode(ValueKey), NewValue);
            return true;
        });
        Target.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode(ValueKey), OldValue); return true; });
        Sender = new(source, sourceLog, RecoveryExecutionOptions.Configuration(Configuration with { Directory = Path.Combine(directory, SourceDirectory) }), RecoveryExecutionOptions.Replica());
        Image = Sender.Create(1, 1);
    }

    private ZoneTreeStore Store(string name) => new(new(Path.Combine(directory, name)) { Incarnation = Configuration.Incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
    internal ReplicaSnapshotStore Receiver(Action<ReplicaCrashBoundary>? observer = null)
        => new(Target, TargetLog, RecoveryExecutionOptions.Configuration(Configuration with { Directory = Path.Combine(directory, TargetDirectory) }), RecoveryExecutionOptions.Replica(), observer);
    internal void Transfer(ReplicaSnapshotStore receiver, ReplicaSnapshot? image = null)
    {
        var snapshot = image ?? Image;
        var offset = receiver.Begin(snapshot);
        while (offset < snapshot.Length)
        {
            offset = receiver.Append(snapshot.TransferId, offset, Sender.ReadChunk(snapshot.TransferId, offset, Configuration.SnapshotChunkBytes));
        }
    }

    public void Dispose()
    {
        TargetLog.Dispose();
        sourceLog.Dispose();
        targetLogStore.Dispose();
        sourceLogStore.Dispose();
        Target.Dispose();
        source.Dispose();
        Directory.Delete(directory, true);
    }
}

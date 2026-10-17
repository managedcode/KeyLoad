using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
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

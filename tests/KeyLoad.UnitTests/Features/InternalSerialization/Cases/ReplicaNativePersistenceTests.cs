using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-004/005: genuine native replica metadata obeys budgets, replay and version fences.</summary>
internal sealed class ReplicaNativePersistenceTests
{
    private const int LegacyVersion = 1;
    private const string Principal = "principal";
    private const string Payload = " { \"text\" : \"Україна 🙂\", \"n\" : 1.00 } ";
    private const string CanonicalDirectory = "canonical";

    [Test]
    [Arguments(ReplicaMalformedVoteShape.Duplicate)]
    [Arguments(ReplicaMalformedVoteShape.Missing)]
    [Arguments(ReplicaMalformedVoteShape.Unknown)]
    [Arguments(ReplicaMalformedVoteShape.WrongType)]
    public async Task NativeHardStateSchemaRejectionPreservesExactStoreAndJournal(ReplicaMalformedVoteShape shape)
    {
        using var files = new ReplicaNativeFiles();
        using var store = files.Open();
        var state = new ReplicaHardState(ReplicaProtocol.FormatVersion, files.Configuration.Incarnation, 0, null, 0, 0, null);
        var bytes = ReplicaNativeFixtureWriter.State(shape, state);
        var key = KeyCodec.Encode(ReplicaProtocol.StateKey);
        store.Commit((tx, _) => { tx.Put(key, bytes); return true; });
        var journal = await File.ReadAllBytesAsync(files.JournalPath);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration));
        }).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(files.JournalPath)).IsEquivalentTo(journal, CollectionOrdering.Matching);
    }

    [Test]
    public async Task DurableNativeVoteCommitAndSemanticRetrySurviveReopen()
    {
        using var files = new ReplicaNativeFiles();
        using var canonical = files.Open(CanonicalDirectory);
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        var operation = database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.Batch, Principal,
            DateTimeOffset.UnixEpoch, Payload));
        using (var store = files.Open())
        using (var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration), canonicalDatabase: database))
        {
            log.SaveTermAndVote(2, files.Configuration.LocalId);
            log.Append([new(1, 2, operation), new(2, 2, operation)]);
            log.Commit(1);
            // Equal operation values have different CLR object references after replay.
            log.Append([new(1, 2, operation with { }), new(2, 2, operation with { })]);
            await Assert.That(log.State.LastIndex).IsEqualTo(2);
        }
        using var reopened = files.Open();
        using var recovered = new DurableReplicaLog(reopened, UnitExecutionOptions.ReplicaConfiguration(files.Configuration), canonicalDatabase: database);
        await Assert.That(recovered.State.Version).IsEqualTo(ReplicaProtocol.FormatVersion);
        await Assert.That(recovered.State.VotedFor).IsEqualTo(files.Configuration.LocalId);
        await Assert.That(recovered.State.CommittedIndex).IsEqualTo(1);
        var persistedOperation = recovered.ReadEntry(1)!.Operation!;
        await Assert.That(persistedOperation.Id).IsEqualTo(operation.Id);
        await Assert.That(persistedOperation.Kind).IsEqualTo(operation.Kind);
        await Assert.That(persistedOperation.PrincipalId).IsEqualTo(operation.PrincipalId);
        await Assert.That(persistedOperation.EvaluatedAt).IsEqualTo(operation.EvaluatedAt);
        await Assert.That(persistedOperation.PayloadJson).IsEqualTo(operation.PayloadJson);
        await Assert.That(persistedOperation.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
        await Assert.That(recovered.ReadEntry(2)!.Operation!.PayloadJson).IsEqualTo(Payload);
        var persisted = reopened.Read(view => view.ReadOwnedValue(KeyCodec.Encode(ReplicaProtocol.StateKey)))!;
        await Assert.That(ReplicaProtocolCodec.Deserialize<ReplicaHardState>(persisted)).IsEqualTo(recovered.State);
        recovered.SaveTermAndVote(3, null);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => recovered.Append([new(1, 3, null)])).Code)
            .IsEqualTo(ErrorCode.Conflict);
    }

    [Test]
    public async Task ExactNativeBatchFitsAndOneByteLessRejectsWithoutAppending()
    {
        using var files = new ReplicaNativeFiles();
        ImmutableArray<ReplicaEntry> entries = [new(1, 1, null), new(2, 1, null)];
        var bytes = checked((int)ReplicaProtocolCodec.MeasureEntries(entries));
        var configuration = files.Configuration with { MaxAppendBytes = bytes };
        using var store = files.Open();
        using var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(configuration));
        log.SaveTermAndVote(1, null);
        log.Append(entries);
        var read = log.Read(1, entries.Length, bytes);
        await Assert.That(read).IsEquivalentTo(entries, CollectionOrdering.Matching);
        await Assert.That(ReplicaProtocolCodec.SerializeEntries(read).Length).IsEqualTo(bytes);
        await Assert.That(log.Read(1, entries.Length, bytes - 1).Length).IsEqualTo(1);
        var singleBytes = checked((int)ReplicaProtocolCodec.MeasureEntries([entries[0]]));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => log.Read(1, 1, singleBytes - 1)).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
        using var rejectedFiles = new ReplicaNativeFiles();
        using var rejectedStore = rejectedFiles.Open();
        using var rejected = new DurableReplicaLog(rejectedStore, UnitExecutionOptions.ReplicaConfiguration(rejectedFiles.Configuration with { MaxAppendBytes = bytes - 1 }));
        rejected.SaveTermAndVote(1, null);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => rejected.Append(entries)).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(rejected.State.LastIndex).IsEqualTo(0);
        await Assert.That(rejected.ReadEntry(1)).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LegacyEncodingOrNativeOldStateVersionPreservesOriginalStore(bool nativeEncoding)
    {
        using var files = new ReplicaNativeFiles();
        using var store = files.Open();
        var legacy = new ReplicaHardState(LegacyVersion, files.Configuration.Incarnation, 0, null, 0, 0, null);
        var bytes = nativeEncoding ? ReplicaProtocolCodec.Serialize(legacy) : JsonDefaults.Serialize(legacy);
        var key = KeyCodec.Encode(ReplicaProtocol.StateKey);
        store.Commit((tx, _) => { tx.Put(key, bytes); return true; });
        var journal = await File.ReadAllBytesAsync(files.JournalPath);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(files.Configuration));
        }).Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(files.JournalPath)).IsEquivalentTo(journal, CollectionOrdering.Matching);
    }

    [Test]
    public async Task NativeBenchmarkAuthorityReopensAndRejectsDifferentMembership()
    {
        using var files = new ReplicaNativeFiles();
        var configuration = files.Configuration with { BenchmarkTopology = true };
        using (var store = files.Open())
        using (var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(configuration)))
        {
            log.SaveTermAndVote(1, configuration.LocalId);
        }
        using var reopened = files.Open();
        using (var accepted = new DurableReplicaLog(reopened, UnitExecutionOptions.ReplicaConfiguration(configuration)))
        {
            await Assert.That(accepted.State.Term).IsEqualTo(1);
        }
        var other = configuration with { VoterIds = [configuration.VoterIds[0], configuration.VoterIds[2], configuration.VoterIds[1]] };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = new DurableReplicaLog(reopened, UnitExecutionOptions.ReplicaConfiguration(other));
        }).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }
}

internal sealed class ReplicaNativeFiles : IDisposable
{
    private const string DirectoryPrefix = "keyload-native-replica-";
    private const string GuidFormat = "N";
    private const string VoterA = "voter-a";
    private const string VoterB = "voter-b";
    private const string VoterC = "voter-c";
    private const string JournalFileName = "commands.wal";
    private const int AppendBytes = 4_096;
    private const int SigningKeyBytes = 32;
    private readonly string directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    internal ReplicaConfiguration Configuration { get; }
    internal ReadOnlyMemory<byte> SigningKey { get; }
    internal string JournalPath => Path.Combine(Configuration.Directory, JournalFileName);

    internal ReplicaNativeFiles(Guid? incarnation = null, ReadOnlyMemory<byte>? signingKey = null)
    {
        Configuration = new(VoterA, [VoterA, VoterB, VoterC], Path.Combine(directory, ReplicaProtocol.ReplicaDirectory),
            incarnation ?? Guid.NewGuid())
        { MaxAppendBytes = AppendBytes };
        SigningKey = signingKey?.ToArray() ?? RandomNumberGenerator.GetBytes(SigningKeyBytes);
    }

    internal ZoneTreeStore Open() => new(new(Configuration.Directory) { Incarnation = Configuration.Incarnation, SigningKey = SigningKey }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
    internal ZoneTreeStore Open(string name) => new(new(Path.Combine(directory, name))
    { Incarnation = Configuration.Incarnation, SigningKey = SigningKey }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
    public void Dispose() => Directory.Delete(directory, true);
}

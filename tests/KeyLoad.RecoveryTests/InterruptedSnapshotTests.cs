using System.Security.Cryptography;
using DotNext.IO;
using DotNext.IO.Log;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

public sealed class InterruptedSnapshotTests
{
    [Theory]
    [InlineData("materialized-image")]
    [InlineData("truncated-intent")]
    [InlineData("scope-intent")]
    [InlineData("published-corruption")]
    public async Task CorruptPublishedImageOrUncertainIntentFailsClosedWithoutDiscardingMaterializedState(string damage)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-snapshot-corruption-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new ZoneTreeStore(new(Path.Combine(root, "database")));
            var database = Database(store);
            store.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 4L); return true; });
            var snapshots = Path.Combine(root, "snapshots"); Directory.CreateDirectory(snapshots);
            var path = Path.Combine(snapshots, "4-1"); store.CreateSnapshot(path, 4);
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            if (damage is "materialized-image" or "published-corruption")
            { bytes[^1] ^= 1; await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken); }
            var intentPath = Path.Combine(snapshots, "incoming");
            if (damage != "published-corruption")
            {
                var intent = damage == "truncated-intent" ? "{"u8.ToArray() : JsonDefaults.Serialize(new
                { Version = 1, Incarnation = damage == "scope-intent" ? Guid.NewGuid() : store.Identity.Incarnation, Index = 4L, Term = 1L });
                await File.WriteAllBytesAsync(intentPath, intent, TestContext.Current.CancellationToken);
            }
            await using var machine = new ReplicatedStateMachine(database, new(snapshots), 2);
            var error = await Assert.ThrowsAsync<KeyLoadException>(() => machine.RestoreAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(ErrorCode.Corruption, error.Code); Assert.Equal(4, database.LastApplied);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal(damage != "published-corruption", File.Exists(intentPath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("interrupted")]
    [InlineData("cancelled")]
    [InlineData("corrupt")]
    [InlineData("scope")]
    public async Task RejectedSnapshotPreservesThePreviousCommittedTailAcrossReopenAndValidRetry(string failure)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-interrupted-snapshot-" + Guid.NewGuid().ToString("N"));
        var incarnation = Guid.NewGuid(); var key = RandomNumberGenerator.GetBytes(32);
        var token = TestContext.Current.CancellationToken;
        var time = DateTimeOffset.UtcNow;
        var operations = Enumerable.Range(1, 5).Select(index => new ReplicatedOperation(Guid.NewGuid(), OperationKind.SetDispatch,
            "root", time.AddMilliseconds(index), index % 2 == 1 ? "true" : "false")).ToArray();
        try
        {
            using var sourceStore = Store("source", incarnation);
            await using var sourceMachine = Machine(Database(sourceStore), "source");
            await sourceMachine.RestoreAsync(token);
            await using var sourceLog = Log("source", sourceMachine);
            await Populate(sourceLog, operations, token);
            var snapshot = Assert.IsAssignableFrom<ISnapshot>(((IStateMachine)sourceMachine).Snapshot);
            Assert.Equal(4, snapshot.Index);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "source", "snapshots", "4-1"), token);
            if (failure == "corrupt") bytes[^1] ^= 1;
            if (failure == "scope")
            {
                using var foreignStore = Store("foreign", Guid.NewGuid());
                await using var foreignMachine = Machine(Database(foreignStore), "foreign");
                await foreignMachine.RestoreAsync(token);
                await using var foreignLog = Log("foreign", foreignMachine);
                await Populate(foreignLog, operations, token);
                bytes = await File.ReadAllBytesAsync(Path.Combine(root, "foreign", "snapshots", "4-1"), token);
            }
            using (var targetStore = Store("target", incarnation))
            {
                var database = Database(targetStore);
                await using var machine = Machine(database, "target");
                await machine.RestoreAsync(token);
                await using var log = Log("target", machine);
                await Populate(log, operations[..3], token);
                var rejected = new FaultySnapshot(bytes, failure);
                var error = await Record.ExceptionAsync(async () => await ((IAuditTrail<IRaftLogEntry>)log).AppendAsync(rejected, 4, token));
                Assert.NotNull(error);
                if (failure == "interrupted") Assert.IsAssignableFrom<IOException>(error);
                else if (failure == "cancelled") Assert.IsAssignableFrom<OperationCanceledException>(error);
                else Assert.Equal(failure == "scope" ? ErrorCode.TokenInvalidated : ErrorCode.Corruption,
                    Assert.IsType<KeyLoadException>(error).Code);
                Assert.Equal(3, log.LastCommittedEntryIndex); Assert.Equal(3, database.LastApplied);
                Assert.Equal(2, ((IStateMachine)machine).Snapshot!.Index);
                Assert.Equal(0, targetStore.Identity.ReadGeneration); Assert.True(Paused(targetStore));
                Assert.False(File.Exists(Path.Combine(root, "target", "snapshots", "4-1")));
            }
            using (var reopened = Store("target", incarnation))
            {
                var database = Database(reopened);
                await using var machine = Machine(database, "target");
                await machine.RestoreAsync(token);
                await using var log = Log("target", machine);
                await log.InitializeAsync(token);
                Assert.Equal(3, log.LastCommittedEntryIndex); Assert.Equal(3, database.LastApplied); Assert.True(Paused(reopened));
                await ((IAuditTrail<IRaftLogEntry>)log).AppendAsync(snapshot, 4, token);
                Assert.Equal(4, log.LastCommittedEntryIndex); Assert.Equal(4, database.LastApplied); Assert.False(Paused(reopened));
                Assert.Equal(1, reopened.Identity.ReadGeneration);
            }
            using var verified = Store("target", incarnation);
            var verifiedDatabase = Database(verified);
            await using var verifiedMachine = Machine(verifiedDatabase, "target");
            await verifiedMachine.RestoreAsync(token);
            await using var verifiedLog = Log("target", verifiedMachine);
            await verifiedLog.InitializeAsync(token);
            Assert.Equal(4, verifiedLog.LastCommittedEntryIndex); Assert.Equal(4, verifiedDatabase.LastApplied); Assert.False(Paused(verified));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        ZoneTreeStore Store(string node, Guid cluster) => new(new(Path.Combine(root, node, "database")) { Incarnation = cluster, SigningKey = key });
        ReplicatedStateMachine Machine(DatabaseEngine database, string node) => new(database, new(Path.Combine(root, node, "snapshots")), 2);
        DurableRaftLog Log(string node, IStateMachine machine) => new(new WriteAheadLog.Options
        { Location = Path.Combine(root, node, "raft"), FlushInterval = Timeout.InfiniteTimeSpan, HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64 }, machine);
    }
    private static bool Paused(ZoneTreeStore store) => store.Read(view => JsonDefaults.Deserialize<bool>(view.Get(KeyCodec.Encode("system", "dispatch-paused"))!));
    private static DatabaseEngine Database(ZoneTreeStore store)
    {
        var database = new DatabaseEngine(store, new AuthorizationPolicy());
        database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
            DatabaseEngine.Credential("root", "root", "root.snapshot-test-credential-32-characters"));
        return database;
    }
    private static async Task Populate(DurableRaftLog log, ReplicatedOperation[] operations, CancellationToken token)
    {
        await log.InitializeAsync(token);
        for (var offset = 0; offset < operations.Length; offset++)
        {
            var index = offset + 1;
            await log.AppendAsync(new BinaryLogEntry { Term = 1, Content = JsonDefaults.Serialize(operations[offset]) }, token);
            await log.CommitAsync(index, token); await log.WaitForApplyAsync(index, token);
        }
    }
    private sealed class FaultySnapshot(byte[] bytes, string failure) : IRaftLogEntry
    {
        public bool IsSnapshot => true;
        public long Term => 1;
        public bool IsReusable => false;
        public long? Length => bytes.Length;
        public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token) where TWriter : IAsyncBinaryWriter
        {
            await writer.WriteAsync(bytes.AsMemory(0, failure is "interrupted" or "cancelled" ? 128 : bytes.Length), token: token);
            if (failure == "interrupted") throw new IOException("Injected interrupted snapshot upload.");
            if (failure == "cancelled")
            {
                using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
                cancelled.Cancel(); cancelled.Token.ThrowIfCancellationRequested();
            }
        }
    }
}

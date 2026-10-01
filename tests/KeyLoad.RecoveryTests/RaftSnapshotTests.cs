using System.Security.Cryptography;
using DotNext.IO.Log;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

public sealed class RaftSnapshotTests
{
    private static DatabaseEngine Database(ZoneTreeStore store)
    {
        var database = new DatabaseEngine(store, new AuthorizationPolicy());
        database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
            DatabaseEngine.Credential("root", "root", "root.snapshot-test-credential-32-characters"));
        return database;
    }
    private static DurableRaftLog Log(string directory, IStateMachine machine) => new(new WriteAheadLog.Options
    {
        Location = directory, FlushInterval = Timeout.InfiniteTimeSpan,
        HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64
    }, machine);
    [Fact]
    public async Task SnapshotAppendAcknowledgesWithoutAFollowingAppendAndReopensAtItsCut()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-snapshot-ack-" + Guid.NewGuid().ToString("N"));
        var incarnation = Guid.NewGuid(); var key = RandomNumberGenerator.GetBytes(32);
        var token = TestContext.Current.CancellationToken;
        try
        {
            using var sourceStore = new ZoneTreeStore(new(Path.Combine(root, "source", "database")) { Incarnation = incarnation, SigningKey = key });
            var sourceDatabase = Database(sourceStore);
            await using var sourceMachine = new ReplicatedStateMachine(sourceDatabase, new(Path.Combine(root, "source", "snapshots")), 2);
            await sourceMachine.RestoreAsync(token);
            await using var sourceLog = Log(Path.Combine(root, "source", "raft"), sourceMachine);
            await sourceLog.InitializeAsync(token);
            var time = DateTimeOffset.UtcNow;
            for (var index = 1; index <= 3; index++)
            {
                var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.SetDispatch, "root", time.AddMilliseconds(index), "true");
                await sourceLog.AppendAsync(new BinaryLogEntry { Term = 1, Content = JsonDefaults.Serialize(operation) }, token);
                await sourceLog.CommitAsync(index, token); await sourceLog.WaitForApplyAsync(index, token);
            }
            var snapshot = Assert.IsAssignableFrom<ISnapshot>(((IStateMachine)sourceMachine).Snapshot);
            Assert.Equal(2, snapshot.Index);
            var targetDirectory = Path.Combine(root, "target");
            using (var store = new ZoneTreeStore(new(Path.Combine(targetDirectory, "database")) { Incarnation = incarnation, SigningKey = key }))
            {
                var database = Database(store);
                await using var machine = new ReplicatedStateMachine(database, new(Path.Combine(targetDirectory, "snapshots")), 2);
                await machine.RestoreAsync(token);
                await using var log = Log(Path.Combine(targetDirectory, "raft"), machine);
                await log.InitializeAsync(token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                await ((IAuditTrail<IRaftLogEntry>)log).AppendAsync(snapshot, snapshot.Index, deadline.Token);
                Assert.Equal(2, log.LastCommittedEntryIndex); Assert.Equal(2, database.LastApplied);
                Assert.Equal(1, store.Identity.ReadGeneration);
            }
            using var reopenedStore = new ZoneTreeStore(new(Path.Combine(targetDirectory, "database")) { Incarnation = incarnation, SigningKey = key });
            var reopenedDatabase = Database(reopenedStore);
            await using var reopenedMachine = new ReplicatedStateMachine(reopenedDatabase, new(Path.Combine(targetDirectory, "snapshots")), 2);
            await reopenedMachine.RestoreAsync(token);
            await using var reopenedLog = Log(Path.Combine(targetDirectory, "raft"), reopenedMachine);
            await reopenedLog.InitializeAsync(token);
            Assert.Equal(2, reopenedLog.LastCommittedEntryIndex); Assert.Equal(2, reopenedDatabase.LastApplied);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task NativeRaftSnapshotInstallsOnFreshReplicaAndReopensWithTheCommittedTail()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-native-snapshot-" + Guid.NewGuid().ToString("N"));
        var incarnation = Guid.NewGuid(); var key = RandomNumberGenerator.GetBytes(32);
        var token = TestContext.Current.CancellationToken;
        try
        {
            using var sourceStore = new ZoneTreeStore(new(Path.Combine(root, "source", "database")) { Incarnation = incarnation, SigningKey = key });
            var sourceDatabase = Database(sourceStore);
            await using var sourceMachine = new ReplicatedStateMachine(sourceDatabase, new(Path.Combine(root, "source", "snapshots")), 2);
            await sourceMachine.RestoreAsync(token);
            await using var sourceLog = Log(Path.Combine(root, "source", "raft"), sourceMachine);
            await sourceLog.InitializeAsync(token);
            var time = DateTimeOffset.UtcNow;
            for (var index = 1; index <= 5; index++)
            {
                var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.SetDispatch, "root", time.AddMilliseconds(index), index % 2 == 1 ? "true" : "false");
                Assert.Equal(index, await sourceLog.AppendAsync(new BinaryLogEntry { Term = 1, Content = JsonDefaults.Serialize(operation) }, token));
                await sourceLog.CommitAsync(index, token); await sourceLog.WaitForApplyAsync(index, token);
            }
            Assert.Equal(4, ((IStateMachine)sourceMachine).Snapshot!.Index);
            var targetDirectory = Path.Combine(root, "target");
            Guid nodeId;
            using (var targetStore = new ZoneTreeStore(new(Path.Combine(targetDirectory, "database")) { Incarnation = incarnation, SigningKey = key }))
            {
                var targetDatabase = Database(targetStore); nodeId = targetStore.Identity.NodeId;
                targetStore.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("obsolete"), true); return true; });
                await using var targetMachine = new ReplicatedStateMachine(targetDatabase, new(Path.Combine(targetDirectory, "snapshots")), 2);
                await targetMachine.RestoreAsync(token);
                await using var targetLog = Log(Path.Combine(targetDirectory, "raft"), targetMachine);
                await targetLog.InitializeAsync(token);
                await targetLog.ImportAsync(sourceLog, token);
                Assert.Equal(5, targetDatabase.LastApplied); Assert.Equal(5, targetLog.LastCommittedEntryIndex);
                Assert.Equal(4, ((IStateMachine)targetMachine).Snapshot!.Index);
                Assert.Equal(nodeId, targetStore.Identity.NodeId); Assert.Equal(1, targetStore.Identity.ReadGeneration);
                Assert.Equal(sourceStore.Position, targetStore.Position);
                Assert.True(targetStore.Read(view => JsonDefaults.Deserialize<bool>(view.Get(KeyCodec.Encode("system", "dispatch-paused"))!)));
                Assert.Null(targetStore.Read(view => view.Get(KeyCodec.Encode("obsolete"))));
            }
            using var reopenedStore = new ZoneTreeStore(new(Path.Combine(targetDirectory, "database")) { Incarnation = incarnation, SigningKey = key });
            var reopenedDatabase = Database(reopenedStore);
            await using var reopenedMachine = new ReplicatedStateMachine(reopenedDatabase, new(Path.Combine(targetDirectory, "snapshots")), 2);
            await reopenedMachine.RestoreAsync(token);
            await using var reopenedLog = Log(Path.Combine(targetDirectory, "raft"), reopenedMachine);
            await reopenedLog.InitializeAsync(token);
            Assert.Equal(5, reopenedDatabase.LastApplied); Assert.Equal(5, reopenedLog.LastAppliedIndex);
            Assert.Equal(nodeId, reopenedStore.Identity.NodeId);
            Assert.True(reopenedStore.Read(view => JsonDefaults.Deserialize<bool>(view.Get(KeyCodec.Encode("system", "dispatch-paused"))!)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

using DotNext.IO;
using DotNext.IO.Log;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

public static class NativeSnapshotCrashScenario
{
    public static async Task RunAsync(string root, string boundary)
    {
        var stage = Enum.TryParse<SnapshotInstallStage>(boundary, out var parsed) ? (SnapshotInstallStage?)parsed : null;
        if (stage is null && boundary != "BodyWriting") throw new ArgumentException("Unknown snapshot crash boundary.");
        using var targetStore = new ZoneTreeStore(new(Path.Combine(root, "database")));
        using var sourceStore = new ZoneTreeStore(new(Path.Combine(root, "source", "database"))
        { Incarnation = targetStore.Identity.Incarnation, SigningKey = targetStore.Identity.SigningKey });
        await using var targetMachine = new ReplicatedStateMachine(Database(targetStore), new(Path.Combine(root, "snapshots")), 2,
            (observed, index) =>
            {
                if (observed != stage || index != 4) return;
                Console.WriteLine("snapshot-crash-point"); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite);
            });
        await using var sourceMachine = new ReplicatedStateMachine(Database(sourceStore), new(Path.Combine(root, "source", "snapshots")), 2);
        await targetMachine.RestoreAsync(); await sourceMachine.RestoreAsync();
        await using var targetLog = Log(Path.Combine(root, "raft"), targetMachine);
        await using var sourceLog = Log(Path.Combine(root, "source", "raft"), sourceMachine);
        var time = DateTimeOffset.UtcNow;
        var operations = Enumerable.Range(1, 5).Select(index => new ReplicatedOperation(Guid.NewGuid(), OperationKind.SetDispatch,
            "root", time.AddMilliseconds(index), index % 2 == 1 ? "true" : "false")).ToArray();
        await Populate(targetLog, operations[..3]); await Populate(sourceLog, operations);
        IRaftLogEntry incoming = stage == SnapshotInstallStage.RejectedFilePublished || boundary == "BodyWriting"
            ? new InterruptedEntry(boundary == "BodyWriting") : ((IStateMachine)sourceMachine).Snapshot!;
        await ((IAuditTrail<IRaftLogEntry>)targetLog).AppendAsync(incoming, 4);
        throw new InvalidOperationException("The requested snapshot crash boundary was not reached.");
    }
    public static DatabaseEngine Database(ZoneTreeStore store)
    {
        var database = new DatabaseEngine(store, new AuthorizationPolicy());
        database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
            DatabaseEngine.Credential("root", "root", "root.snapshot-test-credential-32-characters"));
        return database;
    }
    private static DurableRaftLog Log(string path, IStateMachine machine) => new(new WriteAheadLog.Options
    { Location = path, FlushInterval = Timeout.InfiniteTimeSpan, HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64,
        MemoryManagement = WriteAheadLog.MemoryManagementStrategy.PrivateMemory }, machine);
    private static async Task Populate(DurableRaftLog log, ReplicatedOperation[] operations)
    {
        await log.InitializeAsync();
        for (var offset = 0; offset < operations.Length; offset++)
        {
            var index = offset + 1;
            await log.AppendAsync(new BinaryLogEntry { Term = 1, Content = JsonDefaults.Serialize(operations[offset]) });
            await log.CommitAsync(index); await log.WaitForApplyAsync(index);
        }
    }
    private sealed class InterruptedEntry(bool pauseWhileWriting) : IRaftLogEntry
    {
        public bool IsSnapshot => true;
        public long Term => 1;
        public bool IsReusable => false;
        public long? Length => pauseWhileWriting ? 1_048_576 : 128;
        public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token) where TWriter : IAsyncBinaryWriter
        {
            await writer.WriteAsync(new byte[pauseWhileWriting ? 131_072 : 128], token: token);
            if (pauseWhileWriting)
            { Console.WriteLine("snapshot-crash-point"); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite); }
            throw new IOException("Injected interrupted snapshot transfer.");
        }
    }
}

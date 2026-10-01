using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Replication;
using DotNext.IO.Log;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

var directory = args[0];
if (args[1] == "raft-append")
{
    await using var log = new DurableRaftLog(new WriteAheadLog.Options
    {
        Location = directory, FlushInterval = Timeout.InfiniteTimeSpan,
        MemoryManagement = WriteAheadLog.MemoryManagementStrategy.PrivateMemory,
        HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64
    }, IStateMachine.CreateNoOp());
    IPersistentState state = log;
    await state.InitializeAsync();
    var entry = new BinaryLogEntry { Content = new byte[] { 1, 2, 3 }, Term = 1 };
    if (args[2] == "indexed") await state.AppendAsync(entry, 1);
    else if (args[2] == "next") await state.AppendAsync(entry);
    else if (args[2] == "batch") await state.AppendAsync(new LogEntryProducer<BinaryLogEntry>([entry]), 1);
    else if (args[2] == "append-and-commit") await state.AppendAndCommitAsync(new LogEntryProducer<BinaryLogEntry>([entry]), 1, false, 1);
    else throw new ArgumentException("Unknown Raft append mode.");
    Console.WriteLine("raft-ack"); Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
    return;
}
var stage = Enum.Parse<CommitStage>(args[1]);
var mutationIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
var mode = args.Length > 3 ? args[3] : "commit";
var armed = mode == "commit";
using var store = new ZoneTreeStore(new(directory)
{
    FaultObserver = (observed, position, index) =>
    {
        if (armed && position == 2 && observed == stage && (stage != CommitStage.MutationApplied || index == mutationIndex))
        {
            Console.WriteLine("crash-point"); Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
    }
});
store.Commit((tx, _) => { for (var i = 0; i < 3; i++) tx.PutRecord(KeyCodec.Encode("item", (long)i), 0);
    if (mode == "install") tx.PutRecord(KeyCodec.Encode("obsolete"), true); return true; });
store.Commit((tx, _) => { for (var i = 0; i < 3; i++) tx.PutRecord(KeyCodec.Encode("item", (long)i), 1); return true; });
if (mode == "compact") { armed = true; store.Compact(); }
else if (mode == "install")
{
    var snapshot = Path.Combine(directory, "incoming.snapshot");
    using (var source = new ZoneTreeStore(new(Path.Combine(directory, "snapshot-source"))
        { Incarnation = store.Identity.Incarnation, SigningKey = store.Identity.SigningKey }))
    {
        source.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 8L); return true; });
        source.Commit((tx, _) => { for (var i = 0; i < 3; i++) tx.PutRecord(KeyCodec.Encode("item", (long)i), 2);
            tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 9L); return true; });
        source.CreateSnapshot(snapshot, 9);
    }
    armed = true; store.InstallSnapshot(snapshot, 9);
}
Console.WriteLine("ack"); Console.Out.Flush();
Thread.Sleep(Timeout.Infinite);
public sealed class CrashHostMarker;

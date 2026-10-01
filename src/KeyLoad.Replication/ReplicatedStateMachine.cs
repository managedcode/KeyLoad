using System.Buffers;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

public sealed class ReplicatedStateMachine(DatabaseEngine database) : IStateMachine
{
    public ISnapshot? Snapshot => null;
    public ValueTask ReclaimGarbageAsync(long watermark, CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask<long> ApplyAsync(LogEntry entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (entry.IsSnapshot) throw Errors.Fail(ErrorCode.UnsupportedCapability, "Snapshot installation is not enabled for this format.");
        if (entry.Index <= database.LastApplied) return ValueTask.FromResult(database.LastApplied);
        if (entry.IsConfiguration || entry.Length is 0)
        {
            database.Store.Commit((tx, _) => { tx.PutRecord(KeySpace.Applied, entry.Index); return true; });
        }
        else if (entry.TryGetPayload(out var payload) && payload.Length <= database.Limits.MaxBatchBytes + 65_536)
        {
            var operation = JsonDefaults.Deserialize<ReplicatedOperation>(payload.ToArray());
            database.Apply(operation, entry.Index);
        }
        else throw Errors.Fail(ErrorCode.Corruption, "A replication entry is invalid or exceeds its budget.");
        return ValueTask.FromResult(entry.Index);
    }
}

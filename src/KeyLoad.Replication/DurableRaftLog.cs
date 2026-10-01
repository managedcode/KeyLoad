using DotNext.IO.Log;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

namespace KeyLoad.Replication;

/// <summary>
/// A follower append response is released only after the new tail is flushed.
/// FlushOnCommit alone covers committed entries and is insufficient for an AppendEntries acknowledgement.
/// </summary>
public sealed class DurableRaftLog(WriteAheadLog.Options options, IStateMachine stateMachine)
    : WriteAheadLog(options, stateMachine), IAuditTrail<IRaftLogEntry>
{
    public long FlushedAppendCount { get; private set; }
    public new async ValueTask AppendAsync<T>(ILogEntryProducer<T> entries, long startIndex, bool skipCommitted = false, CancellationToken token = default)
        where T : IRaftLogEntry
    {
        await base.AppendAsync(entries, startIndex, skipCommitted, token).ConfigureAwait(false);
        await FlushAsync(token).ConfigureAwait(false);
        FlushedAppendCount++;
    }
    public new async ValueTask AppendAsync<T>(T entry, long startIndex, CancellationToken token = default) where T : IRaftLogEntry
    {
        await base.AppendAsync(entry, startIndex, token).ConfigureAwait(false);
        await FlushAsync(token).ConfigureAwait(false);
        FlushedAppendCount++;
    }
    public new async ValueTask<long> AppendAsync<T>(T entry, CancellationToken token = default) where T : IRaftLogEntry
    {
        var index = await base.AppendAsync(entry, token).ConfigureAwait(false);
        await FlushAsync(token).ConfigureAwait(false);
        FlushedAppendCount++;
        return index;
    }
    public async ValueTask<long> AppendAndCommitAsync<T>(ILogEntryProducer<T> entries, long startIndex, bool skipCommitted,
        long commitIndex, CancellationToken token = default) where T : IRaftLogEntry
    {
        await AppendAsync(entries, startIndex, skipCommitted, token).ConfigureAwait(false);
        return await CommitAsync(commitIndex, token).ConfigureAwait(false);
    }
}

using DotNext.IO.Log;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

namespace KeyLoad.Replication;

/// <summary>
/// Explicit, serialized native checkpoints establish the follower append and snapshot acknowledgement boundary.
/// </summary>
public sealed class DurableRaftLog(WriteAheadLog.Options options, IStateMachine stateMachine)
    : WriteAheadLog(RequireManualCheckpoints(options), stateMachine), IAuditTrail<IRaftLogEntry>
{
    private readonly SemaphoreSlim persistence = new(1, 1);
    public long FlushedAppendCount { get; private set; }
    private static WriteAheadLog.Options RequireManualCheckpoints(WriteAheadLog.Options options)
        => options.FlushInterval == Timeout.InfiniteTimeSpan ? options
            : throw new ArgumentException("DurableRaftLog requires explicit checkpoints: set FlushInterval to Timeout.InfiniteTimeSpan.", nameof(options));
    public new async ValueTask AppendAsync<T>(ILogEntryProducer<T> entries, long startIndex, bool skipCommitted = false, CancellationToken token = default)
        where T : IRaftLogEntry
    {
        await persistence.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await base.AppendAsync(entries, startIndex, skipCommitted, token).ConfigureAwait(false);
            await base.FlushAsync(token).ConfigureAwait(false);
            FlushedAppendCount++;
        }
        finally { persistence.Release(); }
    }
    public new async ValueTask AppendAsync<T>(T entry, long startIndex, CancellationToken token = default) where T : IRaftLogEntry
    {
        await persistence.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await base.AppendAsync(entry, startIndex, token).ConfigureAwait(false);
            // The explicit checkpoint also completes after a snapshot, without waiting for a background append signal.
            await base.FlushAsync(token).ConfigureAwait(false);
            FlushedAppendCount++;
        }
        finally { persistence.Release(); }
    }
    public new async ValueTask<long> AppendAsync<T>(T entry, CancellationToken token = default) where T : IRaftLogEntry
    {
        await persistence.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var index = await base.AppendAsync(entry, token).ConfigureAwait(false);
            await base.FlushAsync(token).ConfigureAwait(false);
            FlushedAppendCount++;
            return index;
        }
        finally { persistence.Release(); }
    }
    public new async ValueTask<long> CommitAsync(long endIndex, CancellationToken token = default)
    {
        await persistence.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var committed = await base.CommitAsync(endIndex, token).ConfigureAwait(false);
            await base.FlushAsync(token).ConfigureAwait(false);
            return committed;
        }
        finally { persistence.Release(); }
    }
    public async ValueTask<long> AppendAndCommitAsync<T>(ILogEntryProducer<T> entries, long startIndex, bool skipCommitted,
        long commitIndex, CancellationToken token = default) where T : IRaftLogEntry
    {
        await persistence.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await base.AppendAsync(entries, startIndex, skipCommitted, token).ConfigureAwait(false);
            var committed = await base.CommitAsync(commitIndex, token).ConfigureAwait(false);
            await base.FlushAsync(token).ConfigureAwait(false);
            FlushedAppendCount++;
            return committed;
        }
        finally { persistence.Release(); }
    }
}

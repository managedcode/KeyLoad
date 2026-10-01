using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

public sealed class RaftLogTests
{
    [Fact]
    public async Task RaftInterfaceDispatchFlushesUncommittedFollowerAppendBeforeReturning()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-raft-flush-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var log = new DurableRaftLog(new WriteAheadLog.Options
            { Location = directory, FlushInterval = TimeSpan.Zero, FlushOnCommit = true, HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64 }, IStateMachine.CreateNoOp()))
            {
                IPersistentState state = log;
                await state.InitializeAsync(TestContext.Current.CancellationToken);
                await state.AppendAsync(new BinaryLogEntry { Content = new byte[] { 1, 2, 3 }, Term = 1 }, 1, TestContext.Current.CancellationToken);
                Assert.Equal(1, log.FlushedAppendCount);
                Assert.Equal(0, state.LastCommittedEntryIndex);
                Assert.Equal(1, state.LastEntryIndex);
            }
            await using var reopened = new DurableRaftLog(new WriteAheadLog.Options
            { Location = directory, FlushInterval = TimeSpan.Zero, FlushOnCommit = true, HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64 }, IStateMachine.CreateNoOp());
            await reopened.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, reopened.LastEntryIndex);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

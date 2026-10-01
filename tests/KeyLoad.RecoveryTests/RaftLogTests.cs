using System.Buffers;
using System.Diagnostics;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

public sealed class RaftLogTests
{
    [Theory]
    [InlineData("indexed")]
    [InlineData("next")]
    [InlineData("batch")]
    [InlineData("append-and-commit")]
    public async Task KilledProcessPreservesTheAcknowledgedNativeTailWithoutGracefulDispose(string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-raft-kill-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, directory, "raft-append", mode }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var acknowledgement = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(acknowledgement == "raft-ack", acknowledgement ?? await errors);
            process.Kill(); await process.WaitForExitAsync(timeout.Token);
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    foreach (var name in new[] { "checkpoint", "state" })
                        using (File.Open(Path.Combine(directory, name), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    break;
                }
                catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(25, timeout.Token); }
            }
            await using var log = new DurableRaftLog(new WriteAheadLog.Options
            {
                Location = directory, FlushInterval = Timeout.InfiniteTimeSpan,
                MemoryManagement = WriteAheadLog.MemoryManagementStrategy.PrivateMemory,
                HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64
            }, IStateMachine.CreateNoOp());
            await log.InitializeAsync(timeout.Token);
            Assert.Equal(1, log.LastEntryIndex);
            Assert.Equal(mode == "append-and-commit" ? 1 : 0, log.LastCommittedEntryIndex);
            using var reader = await log.ReadAsync(1, 1, timeout.Token);
            Assert.True(reader[0].TryGetPayload(out var payload));
            Assert.Equal(new byte[] { 1, 2, 3 }, payload.ToArray());
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); }
            await errors;
            var elapsed = Stopwatch.StartNew();
            while (Directory.Exists(directory))
            {
                try { Directory.Delete(directory, true); }
                catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(25, TestContext.Current.CancellationToken); }
            }
        }
    }
    [Fact]
    public async Task RaftInterfaceDispatchFlushesUncommittedFollowerAppendBeforeReturning()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-raft-flush-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var log = new DurableRaftLog(new WriteAheadLog.Options
            { Location = directory, FlushInterval = Timeout.InfiniteTimeSpan, HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64 }, IStateMachine.CreateNoOp()))
            {
                IPersistentState state = log;
                await state.InitializeAsync(TestContext.Current.CancellationToken);
                await state.AppendAsync(new BinaryLogEntry { Content = new byte[] { 1, 2, 3 }, Term = 1 }, 1, TestContext.Current.CancellationToken);
                Assert.Equal(1, log.FlushedAppendCount);
                Assert.Equal(0, state.LastCommittedEntryIndex);
                Assert.Equal(1, state.LastEntryIndex);
            }
            await using var reopened = new DurableRaftLog(new WriteAheadLog.Options
            { Location = directory, FlushInterval = Timeout.InfiniteTimeSpan, HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64 }, IStateMachine.CreateNoOp());
            await reopened.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, reopened.LastEntryIndex);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

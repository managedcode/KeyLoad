using System.Diagnostics;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

public sealed partial class RecoveryTests
{
    [Theory]
    [InlineData("IntentPublished", 3)]
    [InlineData("BodyWriting", 3)]
    [InlineData("RejectedFilePublished", 3)]
    [InlineData("SnapshotVerified", 4)]
    [InlineData("IntentCleared", 4)]
    public async Task KilledSnapshotInstallRecoversThePriorTailOrTheCompleteVerifiedImage(string stage, long expectedCut)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-snapshot-install-crash-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, "raft-snapshot-install", stage })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var observed = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(observed == "snapshot-crash-point", observed ?? await errors);
            process.Kill(); await process.WaitForExitAsync(timeout.Token);
            await WaitForKilledProcessFilesAsync(Path.Combine(root, "database"), timeout.Token);
            using (var store = new ZoneTreeStore(new(Path.Combine(root, "database"))))
            {
                var database = NativeSnapshotCrashScenario.Database(store);
                await using var machine = new ReplicatedStateMachine(database, new(Path.Combine(root, "snapshots")), 2);
                await machine.RestoreAsync(timeout.Token);
                await using var log = new DurableRaftLog(new WriteAheadLog.Options
                { Location = Path.Combine(root, "raft"), FlushInterval = Timeout.InfiniteTimeSpan, HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64 }, machine);
                await log.InitializeAsync(timeout.Token);
                Assert.Equal(expectedCut, log.LastCommittedEntryIndex); Assert.Equal(expectedCut, database.LastApplied);
                Assert.Equal(expectedCut == 3, store.Read(view => JsonDefaults.Deserialize<bool>(view.Get(KeyCodec.Encode("system", "dispatch-paused"))!)));
                Assert.Equal(expectedCut == 3 ? 0 : 1, store.Identity.ReadGeneration);
                Assert.False(File.Exists(Path.Combine(root, "snapshots", "incoming")));
                Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "snapshots"), "*.tmp"));
                if (expectedCut == 3) Assert.False(File.Exists(Path.Combine(root, "snapshots", "4-1")));
            }
            // Recovery is idempotent and does not install the same image twice.
            using var again = new ZoneTreeStore(new(Path.Combine(root, "database")));
            var againDatabase = NativeSnapshotCrashScenario.Database(again);
            await using var againMachine = new ReplicatedStateMachine(againDatabase, new(Path.Combine(root, "snapshots")), 2);
            await againMachine.RestoreAsync(timeout.Token);
            Assert.Equal(expectedCut, againDatabase.LastApplied); Assert.Equal(expectedCut == 3 ? 0 : 1, again.Identity.ReadGeneration);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); }
            await errors;
            if (Directory.Exists(root)) await DeleteTrialAsync(root, TestContext.Current.CancellationToken);
        }
    }
}

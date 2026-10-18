using System.Diagnostics;
using KeyLoad.RecoveryHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class StoragePublicationRecoveryTests
{
    [Test]
    [Arguments("compact", CommitStage.SnapshotWritten)]
    [Arguments("compact", CommitStage.SnapshotFlushed)]
    [Arguments("compact", CommitStage.InstallPrepared)]
    [Arguments("compact", CommitStage.JournalSwapped)]
    [Arguments("install", CommitStage.SnapshotWritten)]
    [Arguments("install", CommitStage.SnapshotFlushed)]
    [Arguments("install", CommitStage.InstallPrepared)]
    [Arguments("install", CommitStage.JournalSwapped)]
    public async Task ProcessKillDuringCheckpointPublicationRecoversOneCompleteGeneration(string mode, CommitStage stage)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-checkpoint-crash-" + Guid.NewGuid().ToString("N"));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        try
        {
            await KillCheckpointPublicationProcessAsync(root, mode, stage, cancellationToken);
            await AssertCheckpointRecoveryGenerationAsync(root, mode, stage);
        }
        finally
        {
            await DeleteCheckpointTrialAsync(root, cancellationToken);
        }
    }

    private static async Task KillCheckpointPublicationProcessAsync(string root, string mode, CommitStage stage,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { typeof(RecoveryHostMarker).Assembly.Location, root, stage.ToString(), "0", mode })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        try
        {
            await Assert.That(await process.StandardOutput.ReadLineAsync(timeout.Token)).IsEqualTo("crash-point");
            process.Kill();
            await process.WaitForExitAsync(timeout.Token);
            await StorageRecoveryProcessTests.WaitForKilledProcessFilesAsync(root, timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync(cancellationToken);
            }
        }
    }

    private static async Task AssertCheckpointRecoveryGenerationAsync(string root, string mode, CommitStage stage)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var values = ReadCheckpointValues(store);
        var installed = IsCheckpointGenerationInstalled(mode, stage);
        foreach (var value in values)
        {
            await Assert.That(value).IsEqualTo(installed ? 2 : 1);
        }
        await Assert.That(store.Position).IsEqualTo(2);
        if (mode == "install")
        {
            await AssertInstalledCheckpointMetadataAsync(store, installed);
        }
        await AssertNextCheckpointCommitAsync(store);
    }

    private static int[] ReadCheckpointValues(ZoneTreeStore store) =>
        store.Read(view => Enumerable.Range(0, 3).Select(index =>
            NativeSerialization.Deserialize<int>(view.ReadOwnedValue(KeyCodec.Encode("item", (long)index))!)).ToArray());

    private static bool IsCheckpointGenerationInstalled(string mode, CommitStage stage) =>
        mode == "install" && stage == CommitStage.JournalSwapped;

    private static async Task AssertInstalledCheckpointMetadataAsync(ZoneTreeStore store, bool installed)
    {
        var obsoleteExists = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode("obsolete")) is not null);
        await Assert.That(obsoleteExists).IsEqualTo(!installed);
        var lastApplied = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode("system", "last-applied")) is { } bytes
            ? NativeSerialization.Deserialize<long>(bytes) : 0);
        await Assert.That(lastApplied).IsEqualTo(installed ? 9 : 0);
    }

    private static async Task AssertNextCheckpointCommitAsync(ZoneTreeStore store)
    {
        var next = store.Commit((transaction, position) =>
        {
            transaction.PutRecord(KeyCodec.Encode("after"), true);
            return position;
        });
        await Assert.That(next).IsEqualTo(3);
    }

    private static async Task DeleteCheckpointTrialAsync(string root, CancellationToken cancellationToken)
    {
        if (Directory.Exists(root))
        {
            await DeleteTrialAsync(root, cancellationToken);
        }
    }
    internal static async Task DeleteTrialAsync(string root, CancellationToken cancellationToken)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        while (true)
        {
            try
            {
                Directory.Delete(root, true);
                return;
            }
            catch (IOException) when (clock.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), clock, cancellationToken);
            }
        }
    }
    [Test]
    public async Task CorruptCompleteFrameFailsClosedInsteadOfBeingDiscardedAsATornTail()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-corruption-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()))
            {
                store.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("key"), "value"); return true; });
            }
            var path = Path.Combine(root, "commands.wal");
            var bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OpenCorruptStore(root)).Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(new FileInfo(path).Length).IsEqualTo(bytes.Length);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
    [Test]
    public async Task VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-backup-" + Guid.NewGuid().ToString("N"));
        var backup = root + "-backup";
        var restored = root + "-restored";
        try
        {
            Guid previous;
            using (var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()))
            {
                previous = store.Identity.Incarnation;
                store.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("key"), "value"); return true; });
                store.CreateBackup(backup);
            }
            var identity = ZoneTreeStore.Restore(backup, restored, RecoveryExecutionOptions.StorageExecution());
            await Assert.That(identity.Incarnation).IsNotEqualTo(previous);
            await Assert.That(identity.DispatchPaused).IsTrue();
            if (!OperatingSystem.IsWindows())
            {
                var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                await Assert.That(File.GetUnixFileMode(backup)).IsEqualTo(privateMode);
                await Assert.That(File.GetUnixFileMode(restored)).IsEqualTo(privateMode);
            }
            using var recovered = new ZoneTreeStore(new(restored), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            await Assert.That(recovered.Read(view => NativeSerialization.Deserialize<string>(view.ReadOwnedValue(KeyCodec.Encode("key"))!))).IsEqualTo("value");
            var tampered = await File.ReadAllBytesAsync(Path.Combine(backup, "commands.wal"));
            tampered[^1] ^= 1;
            await File.WriteAllBytesAsync(Path.Combine(backup, "commands.wal"), tampered);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(backup, restored + "-bad", RecoveryExecutionOptions.StorageExecution())).Code).IsEqualTo(ErrorCode.Corruption);
        }
        finally
        {
            foreach (var path in new[] { root, backup, restored })
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }
    }

    private static void OpenCorruptStore(string root)
    {
        using var rejected = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
    }

}

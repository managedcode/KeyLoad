using System.Diagnostics;
using System.Text.Json;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class StorageRecoveryProcessTests
{
    public static IEnumerable<int> Batches => Enumerable.Range(0, 20);
    [Test]
    [MethodDataSource(nameof(Batches))]
    public async Task FiftySeededRealProcessCrashesPreserveAtomicTransactions(int batch)
    {
        var random = new SeededCrashChoice(1701 + batch);
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx")))
        {
            repository = repository.Parent;
        }
        var reports = Path.Combine(File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx"))
            ? repository.FullName : AppContext.BaseDirectory, "artifacts", "qualification");
        Directory.CreateDirectory(reports);
        using var evidence = new StreamWriter(Path.Combine(reports, $"crash-trials-{batch:D2}.jsonl"), false);
        for (var trial = 0; trial < 50; trial++)
        {
            await RunSeededCrashTrialAsync(batch, trial, random, evidence, TestContext.Current!.Execution.CancellationToken);
        }
    }

    private static async Task RunSeededCrashTrialAsync(int batch, int trial, SeededCrashChoice random,
        StreamWriter evidence, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), $"keyload-crash-{batch}-{trial}-{Guid.NewGuid():N}");
        var stage = (CommitStage)random.Next(5);
        var mutationIndex = random.Next(3);
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        using var process = Process.Start(CreateCrashHostStart(root, stage, mutationIndex))!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        Exception? originalFailure = null;
        int[]? values = null;
        try
        {
            await Assert.That(await process.StandardOutput.ReadLineAsync(timeout.Token)).IsEqualTo("crash-point");
            process.Kill();
            await process.WaitForExitAsync(timeout.Token);
            await WaitForKilledProcessFilesAsync(root, timeout.Token);
            values = ReadRecoveredValues(root);
            await AssertAtomicCutAsync(values, batch, trial, stage);
            await RecordCrashTrialAsync(evidence, batch, trial, stage, mutationIndex, values!,
                StorageTrialLease.ActiveStorageTrials, StorageTrialLease.MaximumObservedStorageTrials, timeout.Token);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
            Console.WriteLine($"Seed {1701 + batch}, trial {trial}, stage {stage}: {exception}");
            throw;
        }
        finally
        {
            try
            {
                await CleanupCrashTrialAsync(process, root, cancellationToken);
            }
            catch (IOException error) when (originalFailure is not null)
            {
                Console.WriteLine($"Cleanup after the original failure: {error.Message}");
            }
        }
    }

    private static ProcessStartInfo CreateCrashHostStart(string root, CommitStage stage, int mutationIndex)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(CrashHostMarker).Assembly.Location);
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(stage.ToString());
        start.ArgumentList.Add(mutationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return start;
    }

    private static int[] ReadRecoveredValues(string root)
    {
        using var store = new ZoneTreeStore(new(root));
        return store.Read(view => Enumerable.Range(0, 3)
            .Select(i => NativeSerialization.Deserialize<int>(view.ReadOwnedValue(KeyCodec.Encode("item", (long)i))!)).ToArray());
    }

    private static async Task RecordCrashTrialAsync(StreamWriter evidence, int batch, int trial,
        CommitStage stage, int mutationIndex, int[] values, int activeStorageTrials,
        int maximumObservedStorageTrials, CancellationToken cancellationToken)
    {
        var record = new
        {
            Seed = 1701 + batch,
            Trial = trial,
            Stage = stage.ToString(),
            MutationIndex = mutationIndex,
            Values = values,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ActiveStorageTrials = activeStorageTrials,
            MaximumObservedStorageTrials = maximumObservedStorageTrials
        };
        await evidence.WriteLineAsync(JsonSerializer.Serialize(record).AsMemory(), cancellationToken);
        await evidence.FlushAsync(cancellationToken);
    }

    private static async Task AssertAtomicCutAsync(int[] values, int batch, int trial, CommitStage stage)
    {
        await Assert.That(values.All(value => value == 0) || values.All(value => value == 1))
            .IsTrue().Because($"Non-atomic recovery at seed {batch}, trial {trial}, stage {stage}.");
        if (stage >= CommitStage.JournalFlushed)
        {
            foreach (var value in values)
            {
                await Assert.That(value).IsEqualTo(1);
            }
        }
    }

    private static async Task CleanupCrashTrialAsync(Process process, string root, CancellationToken cancellationToken)
    {
        if (!process.HasExited)
        {
            process.Kill();
            await process.WaitForExitAsync(cancellationToken);
        }
        if (Directory.Exists(root))
        {
            await StoragePublicationRecoveryTests.DeleteTrialAsync(root, cancellationToken);
        }
    }
    internal static Task WaitForKilledProcessFilesAsync(string root, CancellationToken cancellationToken) =>
        KilledProcessFileReadiness.WaitAsync(root, cancellationToken);

    private sealed class SeededCrashChoice(int seed)
    {
        private uint state = unchecked((uint)seed);

        internal int Next(int exclusiveMaximum)
        {
            state += 0x9E3779B9;
            var value = state;
            value = (value ^ (value >> 16)) * 0x85EBCA6B;
            value = (value ^ (value >> 13)) * 0xC2B2AE35;
            value ^= value >> 16;
            return (int)(value % exclusiveMaximum);
        }
    }
}

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
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(), "0", mode })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
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
        using var store = new ZoneTreeStore(new(root));
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
        var started = Stopwatch.StartNew();
        while (true)
        {
            try
            { Directory.Delete(root, true); return; }
            catch (IOException) when (started.Elapsed < TimeSpan.FromSeconds(5))
            { await Task.Delay(25, cancellationToken); }
        }
    }
    [Test]
    public async Task CorruptCompleteFrameFailsClosedInsteadOfBeingDiscardedAsATornTail()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-corruption-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new ZoneTreeStore(new(root)))
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
            using (var store = new ZoneTreeStore(new(root)))
            {
                previous = store.Identity.Incarnation;
                store.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("key"), "value"); return true; });
                store.CreateBackup(backup);
            }
            var identity = ZoneTreeStore.Restore(backup, restored);
            await Assert.That(identity.Incarnation).IsNotEqualTo(previous);
            await Assert.That(identity.DispatchPaused).IsTrue();
            if (!OperatingSystem.IsWindows())
            {
                var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                await Assert.That(File.GetUnixFileMode(backup)).IsEqualTo(privateMode);
                await Assert.That(File.GetUnixFileMode(restored)).IsEqualTo(privateMode);
            }
            using var recovered = new ZoneTreeStore(new(restored));
            await Assert.That(recovered.Read(view => NativeSerialization.Deserialize<string>(view.ReadOwnedValue(KeyCodec.Encode("key"))!))).IsEqualTo("value");
            var tampered = await File.ReadAllBytesAsync(Path.Combine(backup, "commands.wal"));
            tampered[^1] ^= 1;
            await File.WriteAllBytesAsync(Path.Combine(backup, "commands.wal"), tampered);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(backup, restored + "-bad")).Code).IsEqualTo(ErrorCode.Corruption);
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
        using var rejected = new ZoneTreeStore(new(root));
    }

}

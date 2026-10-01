using System.Diagnostics;
using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

public sealed partial class RecoveryTests
{
    public static TheoryData<int> Batches => new(Enumerable.Range(0, 20));
    [Theory]
    [MemberData(nameof(Batches))]
    public async Task FiftySeededRealProcessCrashesPreserveAtomicTransactions(int batch)
    {
        var random = new Random(1701 + batch);
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx"))) repository = repository.Parent;
        var reports = Path.Combine(File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx"))
            ? repository.FullName : AppContext.BaseDirectory, "artifacts", "qualification");
        Directory.CreateDirectory(reports);
        using var evidence = new StreamWriter(Path.Combine(reports, $"crash-trials-{batch:D2}.jsonl"), false);
        for (var trial = 0; trial < 50; trial++)
        {
            var root = Path.Combine(Path.GetTempPath(), $"keyload-crash-{batch}-{trial}-{Guid.NewGuid():N}");
            var stage = (CommitStage)random.Next(5);
            var mutationIndex = random.Next(3);
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(typeof(CrashHostMarker).Assembly.Location);
            start.ArgumentList.Add(root); start.ArgumentList.Add(stage.ToString()); start.ArgumentList.Add(mutationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var process = Process.Start(start)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            Exception? originalFailure = null;
            try
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                Assert.Equal("crash-point", line);
                process.Kill(); await process.WaitForExitAsync(timeout.Token);
                await WaitForKilledProcessFilesAsync(root, timeout.Token);
                using var store = new ZoneTreeStore(new(root));
                var values = store.Read(view => Enumerable.Range(0, 3).Select(i => JsonDefaults.Deserialize<int>(view.Get(KeyCodec.Encode("item", (long)i))!)).ToArray());
                await evidence.WriteLineAsync(JsonSerializer.Serialize(new { Seed = 1701 + batch, Trial = trial, Stage = stage.ToString(),
                    MutationIndex = mutationIndex, Values = values, OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() }).AsMemory(), timeout.Token);
                await evidence.FlushAsync(timeout.Token);
                Assert.True(values.All(v => v == 0) || values.All(v => v == 1), $"Non-atomic recovery at seed {batch}, trial {trial}, stage {stage}.");
                if (stage >= CommitStage.JournalFlushed) Assert.All(values, value => Assert.Equal(1, value));
            }
            catch (Exception exception)
            {
                originalFailure = exception;
                TestContext.Current.TestOutputHelper?.WriteLine($"Seed {1701 + batch}, trial {trial}, stage {stage}: {exception}");
                throw;
            }
            finally
            {
                if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); }
                try { if (Directory.Exists(root)) await DeleteTrialAsync(root, TestContext.Current.CancellationToken); }
                catch (IOException exception) when (originalFailure is not null)
                { TestContext.Current.TestOutputHelper?.WriteLine($"Cleanup after the original failure: {exception.Message}"); }
            }
        }
    }
    private static async Task WaitForKilledProcessFilesAsync(string root, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                foreach (var name in new[] { "owner.lock", "commands.wal" })
                    using (File.Open(Path.Combine(root, name), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return;
            }
            catch (IOException) when (started.Elapsed < TimeSpan.FromSeconds(5))
            { await Task.Delay(25, cancellationToken); }
        }
    }
    [Theory]
    [InlineData("compact", CommitStage.SnapshotWritten)]
    [InlineData("compact", CommitStage.SnapshotFlushed)]
    [InlineData("compact", CommitStage.InstallPrepared)]
    [InlineData("compact", CommitStage.JournalSwapped)]
    [InlineData("install", CommitStage.SnapshotWritten)]
    [InlineData("install", CommitStage.SnapshotFlushed)]
    [InlineData("install", CommitStage.InstallPrepared)]
    [InlineData("install", CommitStage.JournalSwapped)]
    public async Task ProcessKillDuringCheckpointPublicationRecoversOneCompleteGeneration(string mode, CommitStage stage)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-checkpoint-crash-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root, stage.ToString(), "0", mode }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            Assert.Equal("crash-point", await process.StandardOutput.ReadLineAsync(timeout.Token));
            process.Kill(); await process.WaitForExitAsync(timeout.Token);
            await WaitForKilledProcessFilesAsync(root, timeout.Token);
            using var store = new ZoneTreeStore(new(root));
            var values = store.Read(view => Enumerable.Range(0, 3).Select(i => JsonDefaults.Deserialize<int>(view.Get(KeyCodec.Encode("item", (long)i))!)).ToArray());
            var installed = mode == "install" && stage == CommitStage.JournalSwapped;
            Assert.All(values, value => Assert.Equal(installed ? 2 : 1, value));
            Assert.Equal(2, store.Position);
            if (mode == "install")
            {
                Assert.Equal(!installed, store.Read(view => view.Get(KeyCodec.Encode("obsolete")) is not null));
                Assert.Equal(installed ? 9 : 0, store.Read(view => view.Get(KeyCodec.Encode("system", "last-applied")) is { } bytes ? JsonDefaults.Deserialize<long>(bytes) : 0));
            }
            store.Commit((tx, position) => { Assert.Equal(3, position); tx.PutRecord(KeyCodec.Encode("after"), true); return true; });
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); }
            if (Directory.Exists(root)) await DeleteTrialAsync(root, TestContext.Current.CancellationToken);
        }
    }
    private static async Task DeleteTrialAsync(string root, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (true)
        {
            try { Directory.Delete(root, true); return; }
            catch (IOException) when (started.Elapsed < TimeSpan.FromSeconds(5))
            { await Task.Delay(25, cancellationToken); }
        }
    }
    [Fact]
    public void CorruptCompleteFrameFailsClosedInsteadOfBeingDiscardedAsATornTail()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-corruption-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new ZoneTreeStore(new(root))) store.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("key"), "value"); return true; });
            var path = Path.Combine(root, "commands.wal"); var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
            Assert.Equal(ErrorCode.Corruption, Assert.Throws<KeyLoadException>(() => new ZoneTreeStore(new(root))).Code);
            Assert.Equal(bytes.Length, new FileInfo(path).Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void VerifiedBackupRestoresDataWithNewIdentityAndPausedDispatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-backup-" + Guid.NewGuid().ToString("N"));
        var backup = root + "-backup"; var restored = root + "-restored";
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
            Assert.NotEqual(previous, identity.Incarnation); Assert.True(identity.DispatchPaused);
            if (!OperatingSystem.IsWindows())
            {
                var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                Assert.Equal(privateMode, File.GetUnixFileMode(backup));
                Assert.Equal(privateMode, File.GetUnixFileMode(restored));
            }
            using var recovered = new ZoneTreeStore(new(restored));
            Assert.Equal("value", recovered.Read(view => JsonDefaults.Deserialize<string>(view.Get(KeyCodec.Encode("key"))!)));
            var tampered = File.ReadAllBytes(Path.Combine(backup, "commands.wal")); tampered[^1] ^= 1; File.WriteAllBytes(Path.Combine(backup, "commands.wal"), tampered);
            Assert.Equal(ErrorCode.Corruption, Assert.Throws<KeyLoadException>(() => ZoneTreeStore.Restore(backup, restored + "-bad")).Code);
        }
        finally
        { foreach (var path in new[] { root, backup, restored }) if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}

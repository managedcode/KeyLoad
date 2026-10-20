using System.Diagnostics;
using System.Text.Json;
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
        using var process = Process.Start(CreateRecoveryProcessStart(root, stage, mutationIndex))!;
        using var timeoutTimeout = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.ShortRecoveryTimeout, TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
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

    private static ProcessStartInfo CreateRecoveryProcessStart(string root, CommitStage stage, int mutationIndex)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(CrashScenarioDispatcher).Assembly.Location);
        start.ArgumentList.Add(CrashFixtureValues.ProcessMode);
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(stage.ToString());
        start.ArgumentList.Add(mutationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return start;
    }

    private static int[] ReadRecoveredValues(string root)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        return store.Read(view => Enumerable.Range(0, 3)
            .Select(i => OrleansSerialization.Deserialize<int>(view.ReadOwnedValue(KeyCodec.Encode("item", (long)i))!)).ToArray());
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

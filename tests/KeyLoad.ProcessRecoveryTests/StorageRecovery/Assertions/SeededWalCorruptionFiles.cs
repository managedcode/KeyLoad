using KeyLoad.ProcessRecoveryTests.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.ProcessRecoveryTests.StorageRecovery;

internal static class SeededWalCorruptionFiles
{
    internal static async Task RequireFaultedAsync(SeededWalCorruptionTrial trial, CancellationToken token)
    {
        var actual = await RecoveryFileInventory.CaptureAsync(trial.Root, token);
        trial.RefusalFiles.Add(actual);
        await CompareAsync(actual, trial.DamagedFiles);
    }
    internal static async Task RequireRestoredAsync(SeededWalCorruptionTrial trial, CancellationToken token)
    {
        trial.RestoredFiles = await RecoveryFileInventory.CaptureAsync(trial.Root, token);
        await CompareAsync(trial.RestoredFiles, trial.OriginalFiles);
    }
    private static async Task CompareAsync(Dictionary<string, string> actual, Dictionary<string, string> expected)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(actual.Keys).IsEquivalentTo(expected.Keys), failures);
        foreach (var original in expected)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(actual.TryGetValue(original.Key, out var hash)).IsTrue();
                await Assert.That(hash).IsEqualTo(original.Value).Because(original.Key);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task RequireOriginalAuthorityAsync(SeededWalCorruptionTrial trial, CancellationToken token)
    {
        var actual = await RecoveryFileInventory.CaptureAsync(trial.Root, token);
        trial.FollowUpFiles.Add(actual);
        var failures = new List<Exception>();
        foreach (var name in new[] { "identity.json", CommandIdempotencyCrashContract.CommandEvidenceFile,
            CommandIdempotencyCrashContract.ReceiptEvidenceFile, CommandIdempotencyCrashContract.SeedTailEvidenceFile })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(trial.OriginalFiles.TryGetValue(name, out var expected)).IsTrue();
                await Assert.That(actual.TryGetValue(name, out var current)).IsTrue();
                await Assert.That(current).IsEqualTo(expected).Because(name);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task RequireHistoryAsync(SeededWalCorruptionProcessOwner owner)
    {
        string[] phases = [global::KeyLoad.ProcessRecoveryTests.ChecksumProfileProtocol.Seed,
            global::KeyLoad.ProcessRecoveryTests.ChecksumProfileProtocol.CorruptionRefusal,
            global::KeyLoad.ProcessRecoveryTests.ChecksumProfileProtocol.CorruptionRefusal,
            global::KeyLoad.ProcessRecoveryTests.ChecksumProfileProtocol.FirstAppend,
            global::KeyLoad.ProcessRecoveryTests.ChecksumProfileProtocol.SecondAppend,
            global::KeyLoad.ProcessRecoveryTests.ChecksumProfileProtocol.Cold];
        await Assert.That(owner.History.Count).IsEqualTo(phases.Length);
        for (var index = 0; index < phases.Length; index++)
        {
            var actual = owner.History[index];
            await Assert.That(actual.Phase).IsEqualTo(phases[index]);
            await Assert.That(actual.SignalObserved).IsTrue();
            await Assert.That(actual.ExitCode).IsEqualTo(0);
            await Assert.That(actual.OriginalExitAndBothReadersJoined).IsTrue();
            await Assert.That(actual.ProcessClosed).IsTrue();
        }
        await Assert.That(owner.Active).IsNull();
    }
}

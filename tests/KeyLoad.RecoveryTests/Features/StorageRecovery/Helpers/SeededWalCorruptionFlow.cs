using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class SeededWalCorruptionFlow
{
    internal static async Task RunAsync(int seed, CancellationToken callerToken)
    {
        var trial = new SeededWalCorruptionTrial(seed);
        var owner = new SeededWalCorruptionProcessOwner(trial);
        var failures = new List<Exception>();
        using var originalDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken, originalDeadline.Token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await owner.PhaseAsync(NativeChecksumProfileProtocol.Seed, deadline.Token);
            await trial.CaptureAsync(deadline.Token);
            await trial.DamageAsync(deadline.Token);
        }, failures);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ServerFailureObserver.ObserveAsync(() => owner.PhaseAsync(NativeChecksumProfileProtocol.CorruptionRefusal, deadline.Token), failures);
            await ServerFailureObserver.ObserveAsync(() => SeededWalCorruptionFiles.RequireFaultedAsync(trial, deadline.Token), failures);
            ServerFailureObserver.Observe(() => RecoveryFileInventory.AssertNativeHandlesReleased(trial.Root), failures);
        }
        await RepairAsync(trial, failures);
        foreach (var phase in new[] { NativeChecksumProfileProtocol.FirstAppend,
            NativeChecksumProfileProtocol.SecondAppend, NativeChecksumProfileProtocol.Cold })
        {
            await ServerFailureObserver.ObserveAsync(() => owner.PhaseAsync(phase, deadline.Token), failures);
            await ServerFailureObserver.ObserveAsync(() => SeededWalCorruptionFiles.RequireOriginalAuthorityAsync(trial, deadline.Token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => SeededWalCorruptionFiles.RequireHistoryAsync(owner), failures);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(async () => await SeededWalCorruptionEvidence.SaveAsync(trial, owner, failures, cleanup.Token), failures);
        await ServerFailureObserver.ObserveAsync(() => CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(trial.Root, owner.Active, null), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RepairAsync(SeededWalCorruptionTrial trial, List<Exception> failures)
    {
        using var repair = new CancellationTokenSource(TimeSpan.FromSeconds(CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => trial.RepairAsync(repair.Token), failures);
        await ServerFailureObserver.ObserveAsync(() => SeededWalCorruptionFiles.RequireRestoredAsync(trial, repair.Token), failures);
        ServerFailureObserver.Observe(() => RecoveryFileInventory.AssertNativeHandlesReleased(trial.Root), failures);
    }
}

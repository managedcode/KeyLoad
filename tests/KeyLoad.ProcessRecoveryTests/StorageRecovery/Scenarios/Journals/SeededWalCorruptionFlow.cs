using KeyLoad.ProcessRecoveryTests.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.ProcessRecoveryTests.StorageRecovery;

internal static class SeededWalCorruptionFlow
{
    private const int NoFailures = 0;
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;
    internal static async Task RunAsync(int seed, CancellationToken callerToken)
    {
        var trial = new SeededWalCorruptionTrial(seed);
        var owner = new SeededWalCorruptionProcessOwner(trial);
        var failures = new List<Exception>();
        using var originalDeadline = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CommandTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken, originalDeadline.Token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await owner.PhaseAsync(ChecksumProfileProtocol.Seed, deadline.Token);
            await trial.CaptureAsync(deadline.Token);
            await trial.DamageAsync(deadline.Token);
        }, failures);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ServerFailureObserver.ObserveAsync(() => owner.PhaseAsync(ChecksumProfileProtocol.CorruptionRefusal, deadline.Token), failures);
            await ServerFailureObserver.ObserveAsync(() => SeededWalCorruptionFiles.RequireFaultedAsync(trial, deadline.Token), failures);
            ServerFailureObserver.Observe(() => RecoveryFileInventory.AssertHandlesReleased(trial.Root), failures);
        }
        await RepairAsync(trial, failures);
        foreach (var phase in new[] { ChecksumProfileProtocol.FirstAppend,
            ChecksumProfileProtocol.SecondAppend, ChecksumProfileProtocol.Cold })
        {
            await ServerFailureObserver.ObserveAsync(() => owner.PhaseAsync(phase, deadline.Token), failures);
            await ServerFailureObserver.ObserveAsync(() => SeededWalCorruptionFiles.RequireOriginalAuthorityAsync(trial, deadline.Token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => SeededWalCorruptionFiles.RequireHistoryAsync(owner), failures);
        using var cleanup = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CleanupTimeout, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(async () => await SeededWalCorruptionEvidence.SaveAsync(trial, owner, failures, cleanup.Token), failures);
        await CleanupAsync(trial, owner, failures);
        CommandIdempotencyProcessFailureHandling.ThrowFailures(failures);
    }
    internal static async Task CleanupAsync(SeededWalCorruptionTrial trial,
        SeededWalCorruptionProcessOwner owner, List<Exception> failures)
    {
        var primary = failures.Count switch
        {
            NoFailures => null,
            SingleFailure => failures[FirstFailure],
            _ => new AggregateException(failures)
        };
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(
            trial.Root, owner.Active, primary), observed);
        foreach (var failure in observed.Where(failure => !ReferenceEquals(failure, primary)))
        {
            if (failure is AggregateException aggregate && aggregate.InnerExceptions.Any(item => ReferenceEquals(item, primary)))
            {
                foreach (var additional in aggregate.InnerExceptions.Where(item => !ReferenceEquals(item, primary)))
                {
                    CommandIdempotencyProcessFailureHandling.AddDistinct(failures, additional);
                }
            }
            else
            {
                CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
            }
        }
    }
    private static async Task RepairAsync(SeededWalCorruptionTrial trial, List<Exception> failures)
    {
        using var repair = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CleanupTimeout, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => trial.RepairAsync(repair.Token), failures);
        await ServerFailureObserver.ObserveAsync(() => SeededWalCorruptionFiles.RequireRestoredAsync(trial, repair.Token), failures);
        ServerFailureObserver.Observe(() => RecoveryFileInventory.AssertHandlesReleased(trial.Root), failures);
    }
}

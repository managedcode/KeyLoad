using System.Diagnostics;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class SeededWalCorruptionProcessOwner(SeededWalCorruptionTrial trial)
{
    internal List<SeededWalPhaseReceipt> History { get; } = [];
    internal CommandIdempotencyProcessChild? Active
    {
        get; private set;
    }
    internal async Task PhaseAsync(string phase, CancellationToken token)
    {
        if (Active is not null)
        {
            throw new InvalidOperationException("The previous original corruption-trial child is not closed.");
        }
        var signal = phase == NativeChecksumProfileProtocol.CorruptionRefusal
            ? NativeChecksumProfileProtocol.CorruptionRefusedSignal : NativeChecksumProfileProtocol.CompletionSignalPrefix + phase;
        var child = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, signal);
        Active = child;
        child.Start(Start(phase));
        var failures = new List<Exception>();
        var signaled = false;
        int? exit = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            child.ThrowStartupFailure();
            await child.WaitForAcknowledgementAsync(token);
            signaled = true;
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => child.WaitAndJoinAsync(token), failures);
        ServerFailureObserver.Observe(() => exit = child.ExitCode, failures);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), TimeProvider.System);
        await CommandIdempotencyProcess.SettleActiveAsync(child, failures, cleanup.Token);
        History.Add(new(phase, signal, signaled, exit, child.IsSettled, child.IsDisposed));
        if (child.IsSettled && child.IsDisposed)
        {
            Active = null;
        }
        await ServerFailureObserver.ObserveAsync(() => KilledProcessFileReadiness.WaitAsync(trial.Root, cleanup.Token), failures);
        ServerFailureObserver.Observe(() => RecoveryFileInventory.AssertNativeHandlesReleased(trial.Root), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(exit).IsEqualTo(0), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private ProcessStartInfo Start(string phase)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(CrashScenarioDispatcher).Assembly.Location, CrashFixtureValues.ProcessMode,
            NativeChecksumProfileProtocol.Mode, trial.Root, phase })
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }
}

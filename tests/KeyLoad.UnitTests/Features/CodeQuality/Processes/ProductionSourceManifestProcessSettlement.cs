using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal static class ProductionSourceManifestProcessSettlement
{
    internal static async Task<NativeProcessCleanupDeadline> JoinAsync(Process process, bool processStarted,
        Task original, TimeSpan settlementTimeout, TimeProvider timeProvider, TimeSpan terminationGrace,
        List<Exception> failures)
    {
        var deadline = new NativeProcessCleanupDeadline(settlementTimeout, timeProvider);
        try
        {
            await JoinOriginalAsync(process, processStarted, original, deadline, terminationGrace, failures)
                .ConfigureAwait(false);
            await ObserveOriginalAsync(original, failures).ConfigureAwait(false);
            return deadline;
        }
        catch (Exception primary)
        {
            var cleanupFailures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => deadline.DisposeAsync().AsTask(), cleanupFailures)
                .ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(cleanupFailures);
            throw;
        }
    }

    private static async Task JoinOriginalAsync(Process process, bool processStarted, Task original,
        NativeProcessCleanupDeadline deadline, TimeSpan terminationGrace, List<Exception> failures)
    {
        if (NeedsCleanup(original, failures) && processStarted)
        {
            ServerFailureObserver.Observe(() => KillIfRunning(process), failures);
        }
        if (!await deadline.TryJoinAsync(original, terminationGrace).ConfigureAwait(false))
        {
            if (processStarted)
            {
                CloseOriginalReaders(process, failures);
            }
            if (!await deadline.TryJoinAsync(original).ConfigureAwait(false))
            {
                NativeProcessCleanupDeadline.FailStop();
            }
        }
    }

    private static bool NeedsCleanup(Task original, List<Exception> failures)
        => failures.Count != 0 || !original.IsCompleted || !original.IsCompletedSuccessfully;

    private static async Task ObserveOriginalAsync(Task original, List<Exception> failures)
    {
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, observed).ConfigureAwait(false);
        var alreadyRetained = new List<Exception>(failures);
        foreach (var failure in observed)
        {
            var index = alreadyRetained.FindIndex(retained => IsSameFailure(retained, failure));
            if (index >= 0)
            {
                alreadyRetained.RemoveAt(index);
            }
            else
            {
                failures.Add(failure);
            }
        }
    }

    private static bool IsSameFailure(Exception retained, Exception failure)
        => ReferenceEquals(retained, failure) ||
            (retained is TaskCanceledException first && failure is TaskCanceledException second &&
             first.Task is not null && ReferenceEquals(first.Task, second.Task));

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }

    private static void CloseOriginalReaders(Process process, List<Exception> failures)
    {
        if (process.StartInfo.RedirectStandardOutput)
        {
            ServerFailureObserver.Observe(process.StandardOutput.Dispose, failures);
        }
        if (process.StartInfo.RedirectStandardError)
        {
            ServerFailureObserver.Observe(process.StandardError.Dispose, failures);
        }
    }
}

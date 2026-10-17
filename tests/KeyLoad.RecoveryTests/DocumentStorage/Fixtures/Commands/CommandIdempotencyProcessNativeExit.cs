using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class CommandIdempotencyProcessNativeExit
{
    internal static void TryKill(Process process, List<Exception> failures)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception)
        {
            ConfirmKillRace(process, failure, failures);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    internal static async Task ObserveExitAsync(Task? originalExit, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        if (originalExit is null)
        {
            failures.Add(new InvalidOperationException("The original process exit observer did not start."));
            return;
        }
        try
        {
            await originalExit.WaitAsync(cancellationToken);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    internal static bool HasExited(Process process, List<Exception> failures)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
            return false;
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
            return false;
        }
    }

    private static void ConfirmKillRace(Process process, Exception killFailure, List<Exception> failures)
    {
        try
        {
            if (!process.HasExited)
            {
                failures.Add(killFailure);
            }
        }
        catch (Exception confirmationFailure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(confirmationFailure))
        {
            failures.Add(CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(
                killFailure, confirmationFailure)!);
        }
        catch (Exception confirmationFailure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(confirmationFailure))
        {
            failures.Add(CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(
                killFailure, confirmationFailure)!);
        }
    }
}

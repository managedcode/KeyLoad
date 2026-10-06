using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Stops the owned native probe and settles every original process and pipe task.</summary>
internal static class ScaleServerResourceProcessSettlement
{
    internal static async Task SettleAsync(Process process, Task output, Task error, Task exit, Task readers,
        TimeSpan settlement, Func<int, int, int> sendSignal, Exception primary)
    {
        var failures = new List<Exception>();
        await RecordAsync(StopAsync(process, settlement, sendSignal), primary, failures);
        await RecordAsync(exit, primary, failures);
        await RecordAsync(output, primary, failures);
        await RecordAsync(error, primary, failures);
        // The aggregate must also be observed; original pipe failure objects are retained separately.
        await RecordAsync(readers, primary, []);
        ThrowFailures(failures);
    }

    private static async Task StopAsync(Process process, TimeSpan settlement, Func<int, int, int> sendSignal)
    {
        const int TerminationSignal = 15;
        if (process.HasExited)
        {
            return;
        }
        if (OperatingSystem.IsLinux())
        {
            _ = sendSignal(process.Id, TerminationSignal);
        }
        var exited = process.WaitForExitAsync(CancellationToken.None);
        using var timerCancellation = new CancellationTokenSource();
        var timer = Task.Delay(settlement, timerCancellation.Token);
        var failures = new List<Exception>();
        await RecordAsync(WaitUntilStoppedAsync(process, exited, timer), failures);
        await RecordAsync(InvokeAsync(timerCancellation.CancelAsync), failures);
        await JoinTimerAsync(timer, failures, timerCancellation.Token);
        ThrowFailures(failures);
    }

    private static async Task WaitUntilStoppedAsync(Process process, Task exited, Task timer)
    {
        if (await Task.WhenAny(exited, timer) != exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
        await exited;
    }

    private static async Task JoinTimerAsync(Task timer, List<Exception> failures, CancellationToken expectedToken)
    {
        try
        {
            await timer;
        }
        catch (OperationCanceledException failure) when (failure.CancellationToken == expectedToken
            && expectedToken.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (timer.IsFaulted || timer.IsCanceled)
        {
            failures.Add(failure);
        }
    }

    private static async Task InvokeAsync(Func<Task> factory)
        => await factory();

    private static async Task RecordAsync(Task original, List<Exception> failures)
    {
        try
        {
            await original;
        }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            failures.Add(failure);
        }
    }

    private static async Task RecordAsync(Task original, Exception primary, List<Exception> failures)
    {
        try
        {
            await original;
        }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            if (!SameTerminalFailure(primary, failure) && !failures.Any(previous => SameTerminalFailure(previous, failure)))
            {
                failures.Add(failure);
            }
        }
    }

    private static bool SameTerminalFailure(Exception left, Exception right)
        => ReferenceEquals(left, right)
            || left is OperationCanceledException original && right is OperationCanceledException cleanup
                && original.CancellationToken == cleanup.CancellationToken;

    private static void ThrowFailures(List<Exception> failures)
    {
        const int NoFailures = 0;
        const int SingleFailure = 1;
        const int FirstFailureIndex = 0;
        if (failures.Count == SingleFailure)
        {
            ExceptionDispatchInfo.Capture(failures[FirstFailureIndex]).Throw();
        }
        if (failures.Count > NoFailures)
        {
            throw new AggregateException(failures);
        }
    }
}

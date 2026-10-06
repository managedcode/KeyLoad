using System.Diagnostics;
using System.Runtime.ExceptionServices;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageNodeJoined(int ExitCode, string StandardOutput,
    string StandardError, bool ExitJoined, bool OutputJoined, bool ErrorJoined)
{
    internal static NativeCoverageImageNodeJoined Empty { get; } = new(-1, string.Empty, string.Empty,
        false, false, false);
}

internal static class NativeCoverageImageNodeSettlement
{
    private const string SettlementFailure = "The native coverage image materializer did not settle within its cleanup bound.";

    internal static async Task<NativeCoverageImageNodeJoined> SettleAsync(Process process, Task? exit,
        Task<string>? output, Task<string>? error, TimeSpan settlementTimeout, List<Exception> failures)
    {
        ServerFailureObserver.Observe(() => TryStop(process), failures);
        if (exit is null)
        {
            ServerFailureObserver.Observe(() => exit = process.WaitForExitAsync(CancellationToken.None), failures);
        }
        var original = Task.WhenAll(exit ?? Task.CompletedTask, output ?? Task.CompletedTask,
            error ?? Task.CompletedTask);
        await ServerFailureObserver.ObserveAsync(() => original.WaitAsync(settlementTimeout, TimeProvider.System), failures).ConfigureAwait(false);
        if (!original.IsCompleted)
        {
            failures.Add(new TimeoutException(SettlementFailure));
            ServerFailureObserver.Observe(() => TryStop(process), failures);
            ServerFailureObserver.Observe(process.StandardOutput.Dispose, failures);
            ServerFailureObserver.Observe(process.StandardError.Dispose, failures);
        }
        var stdout = await ObserveTextAsync(output, failures).ConfigureAwait(false);
        var stderr = await ObserveTextAsync(error, failures).ConfigureAwait(false);
        var exitJoined = exit?.IsCompleted == true;
        var exitCode = -1;
        if (exitJoined)
        {
            ServerFailureObserver.Observe(() => exitCode = process.HasExited ? process.ExitCode : -1, failures);
        }
        return new(exitCode, stdout.Text, stderr.Text, exitJoined, stdout.Joined, stderr.Joined);
    }

    internal static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }
        var fatal = CqrsRuntimeFailures.FindFatal(new AggregateException(failures));
        if (fatal is not null)
        {
            ExceptionDispatchInfo.Capture(fatal).Throw();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<(string Text, bool Joined)> ObserveTextAsync(Task<string>? original,
        List<Exception> failures)
    {
        if (original is null || !original.IsCompleted)
        {
            return (string.Empty, false);
        }
        await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        return (original.IsCompletedSuccessfully ? await original.ConfigureAwait(false) : string.Empty, true);
    }

    private static void TryStop(Process process)
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
}

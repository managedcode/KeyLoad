using System.Diagnostics;
using System.Runtime.ExceptionServices;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Processes;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Runs the resource callback's engine operation with the existing AppHost process and reader owner.</summary>
internal static class ContainerFaultProcess
{
    private const string Executable = "docker";
    private const string StartFailure = "The owned fault process did not start.";
    private const string JoinedFailure = "The AppHost fault process and original readers failed.";
    private const int MaximumOutputCharacters = 16_384;
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;

    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        IReadOnlyList<string> arguments, IOptions<TestExecutionOptions> options, CancellationToken token)
    {
        var info = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        token.ThrowIfCancellationRequested();
        var process = Process.Start(info) ?? throw new InvalidOperationException(StartFailure);
        return await RunOwnedAsync(process, options, token).ConfigureAwait(false);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunOwnedAsync(Process process,
        IOptions<TestExecutionOptions> options, CancellationToken token)
    {
        Task? exit = null;
        Task<string>? output = null;
        Task<string>? error = null;
        var failures = new List<Exception>();
        Task<(int ExitCode, string Output, string Error)> original;
        try
        {
            original = RunProcessAsync();
            var primary = await OwnedProcessFailureObserver.CaptureAsync(original).ConfigureAwait(false);
            if (primary is not null)
            {
                failures.Add(primary);
                var cleanup = LocalClusterOwnedProcessLifetime.TerminateAndJoinAsync(process,
                    exit ?? Task.CompletedTask, output ?? Task.CompletedTask, error ?? Task.CompletedTask,
                    failures, options, TimeProvider.System);
                var cleanupFailure = await OwnedProcessFailureObserver.CaptureAsync(cleanup).ConfigureAwait(false);
                if (cleanupFailure is not null)
                {
                    failures.Add(cleanupFailure);
                }
            }
        }
        finally
        {
            OwnedProcessFailureObserver.Observe(process.Dispose, failures);
        }
        ThrowFailures(failures);
        return await original.ConfigureAwait(false);

        async Task<(int ExitCode, string Output, string Error)> RunProcessAsync()
        {
            using var deadline = new CancellationTokenSource(options.Value.ApplicationCleanupTimeout, TimeProvider.System);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            exit = process.WaitForExitAsync(CancellationToken.None);
            output = LocalClusterOwnedProcessLifetime.ReadBoundedAsync(process.StandardOutput, MaximumOutputCharacters);
            error = LocalClusterOwnedProcessLifetime.ReadBoundedAsync(process.StandardError, MaximumOutputCharacters);
            await LocalClusterOwnedProcessLifetime.ObserveAsync(exit, output, error, TimeProvider.System, lifetime.Token).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == SingleFailure)
        {
            ExceptionDispatchInfo.Capture(failures[FirstFailure]).Throw();
        }
        if (failures.Count > SingleFailure)
        {
            throw new AggregateException(JoinedFailure, failures);
        }
    }
}

using System.Diagnostics;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Processes;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns the actual native workload child and joins its original process and output readers before returning.</summary>
internal static class BenchmarkSessionClientProcess
{
    internal static async Task<int> RunAsync(IReadOnlyDictionary<string, string> environment,
        string evidence, IOptions<TestExecutionOptions> execution, CancellationToken token)
    {
        const string ResultsDirectory = "NativeTestResults";
        const string MinimumTests = "7";
        var info = ComparisonHostLaunchSettings.Create([], environment);
        foreach (var argument in new[] { "--report-trx", "--results-directory",
            Path.Combine(evidence, ResultsDirectory), "--minimum-expected-tests", MinimumTests })
        {
            info.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = info };
        if (!process.Start())
        {
            throw new InvalidOperationException("The native comparison client did not start.");
        }
        var stdout = new ComparisonHostOutputCapture();
        var stderr = new ComparisonHostOutputCapture();
        var exit = process.WaitForExitAsync(CancellationToken.None);
        var output = stdout.CaptureAsync(process.StandardOutput, CancellationToken.None);
        var error = stderr.CaptureAsync(process.StandardError, CancellationToken.None);
        var failures = new List<Exception>();
        try
        {
            await LocalClusterOwnedProcessLifetime.ObserveAsync(exit, output, error, TimeProvider.System, token);
        }
        catch (Exception primary) when (CqrsRuntimeFailures.FindFatal(primary) is null)
        {
            failures.Add(primary);
            await ServerFailureObserver.ObserveAsync(() => LocalClusterOwnedProcessLifetime.TerminateAndJoinAsync(
                process, exit, output, error, failures, execution, TimeProvider.System), failures);
        }
        catch (Exception primary) when (CqrsRuntimeFailures.FindFatal(primary) is not null)
        {
            failures.Add(primary);
            await ServerFailureObserver.ObserveAsync(() => LocalClusterOwnedProcessLifetime.TerminateAndJoinAsync(
                process, exit, output, error, failures, execution, TimeProvider.System), failures);
        }
        finally
        {
            await Task.WhenAll(exit, output, error).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(evidence, "client-stdout.log"), await output);
            await File.WriteAllTextAsync(Path.Combine(evidence, "client-stderr.log"), await error);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return process.ExitCode;
    }

}

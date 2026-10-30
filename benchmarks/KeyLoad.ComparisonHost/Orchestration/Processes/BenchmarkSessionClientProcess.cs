using System.Diagnostics;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.AppHost.TestInfrastructure.Processes;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Owns the actual execution workload child and joins its original process and output readers before returning.</summary>
internal static class BenchmarkSessionClientProcess
{
    private const string ReportTrxArgument = "--report-trx";
    private const string ResultsDirectoryArgument = "--results-directory";
    private const string MinimumExpectedTestsArgument = "--minimum-expected-tests";
    private const string TheExecutionComparisonClientDidNotStartMessage = "The execution comparison client did not start.";
    private const string ClientStdoutLogFileName = "client-stdout.log";
    private const string ClientStderrLogFileName = "client-stderr.log";

    internal static async Task<int> RunAsync(IReadOnlyDictionary<string, string> environment,
        string evidence, IOptions<TestExecutionOptions> execution, CancellationToken token)
    {
        const string ResultsDirectory = "RuntimeTestResults";
        const string MinimumTests = "13";
        var info = ComparisonHostLaunchSettings.Create([], environment);
        foreach (var argument in new[] { ReportTrxArgument, ResultsDirectoryArgument,
            Path.Combine(evidence, ResultsDirectory), MinimumExpectedTestsArgument, MinimumTests })
        {
            info.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = info };
        if (!process.Start())
        {
            throw new InvalidOperationException(TheExecutionComparisonClientDidNotStartMessage);
        }
        var exit = process.WaitForExitAsync(CancellationToken.None);
        var output = ComparisonHostOutputCapture.CaptureAsync(process.StandardOutput, CancellationToken.None);
        var error = ComparisonHostOutputCapture.CaptureAsync(process.StandardError, CancellationToken.None);
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
            await File.WriteAllTextAsync(Path.Combine(evidence, ClientStdoutLogFileName), await output);
            await File.WriteAllTextAsync(Path.Combine(evidence, ClientStderrLogFileName), await error);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return process.ExitCode;
    }

}

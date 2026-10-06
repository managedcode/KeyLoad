using System.Diagnostics;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Processes;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed class NativeCoverageRf3Cleanup(NativeCoverageRf3Run run,
    NativeCoverageRf3Invocation invocation, IOptions<NativeCoverageExecutionOptions> coverageOptions,
    IOptions<TestExecutionOptions> executionOptions, TimeProvider? provider = null)
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const string DockerExecutable = "docker";
    private const string RemoveCommand = "image";
    private const string RemoveSubcommand = "rm";
    private const string OwnedImageCleanupMessage = "The owned original-node coverage image cleanup failed.";
    private const int SuccessfulExitCode = 0;
    private const int PrimaryFailureCount = 1;
    private readonly NativeCoverageExecutionOptions coverage = coverageOptions.Value;
    private readonly TestExecutionOptions execution = executionOptions.Value;

    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (!coverage.IsValid() || !execution.IsValid())
        { throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection); }
        var start = new ProcessStartInfo(DockerExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(run.ManifestPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(RemoveCommand);
        start.ArgumentList.Add(RemoveSubcommand);
        start.ArgumentList.Add(run.ImageReference);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException(OwnedImageCleanupMessage);
        var output = LocalRf3OwnedProcessLifetime.ReadBoundedAsync(process.StandardOutput, execution.CleanupOutputCharacters);
        var error = LocalRf3OwnedProcessLifetime.ReadBoundedAsync(process.StandardError, execution.CleanupOutputCharacters);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        using var timeout = new AppHostDeadline(coverage.ContainerStopTimeout, timeProvider, cancellationToken);
        try
        {
            await LocalRf3OwnedProcessLifetime.ObserveAsync(exit, output, error, cancellationToken: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
            if (process.ExitCode != SuccessfulExitCode)
            {
                throw new InvalidOperationException(OwnedImageCleanupMessage);
            }
            Directory.Delete(invocation.ContextDirectory, recursive: true);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await LocalRf3OwnedProcessLifetime.TerminateAndJoinAsync(process, exit, output, error, failures,
                executionOptions, timeProvider).ConfigureAwait(false);
            if (failures.Count == PrimaryFailureCount)
            {
                throw;
            }
            throw new AggregateException(OwnedImageCleanupMessage, failures);
        }
    }
}

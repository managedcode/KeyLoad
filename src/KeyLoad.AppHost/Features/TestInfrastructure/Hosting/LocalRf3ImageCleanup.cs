using System.Diagnostics;
using Microsoft.Extensions.Options;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Features.TestInfrastructure.Processes;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Releases only the exact locally built image after the selected RF3 child has stopped.</summary>
internal sealed class LocalRf3ImageCleanup(LocalRf3ImageExecution execution, string scriptPath,
    IOptions<TestExecutionOptions> options)
{
    private readonly TestExecutionOptions policy = options.Value;

    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(policy.ImageCleanupTimeout);
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = execution.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add("cleanup");
        start.ArgumentList.Add(execution.Tag);
        start.ArgumentList.Add(execution.ReceiptPath);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Local RF3 image cleanup process could not start.");

        var output = LocalRf3OwnedProcessLifetime.ReadBoundedAsync(process.StandardOutput, policy.CleanupOutputCharacters);
        var error = LocalRf3OwnedProcessLifetime.ReadBoundedAsync(process.StandardError, policy.CleanupOutputCharacters);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        try
        {
            await LocalRf3OwnedProcessLifetime.ObserveAsync(exit, output, error, timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Local RF3 image cleanup failed.");
            }
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await LocalRf3OwnedProcessLifetime.TerminateAndJoinAsync(process, exit, output, error, failures, options)
                .ConfigureAwait(false);
            if (failures.Count == 1)
            {
                throw;
            }
            throw new AggregateException("Local RF3 image cleanup and process settlement failed.", failures);
        }
    }
}

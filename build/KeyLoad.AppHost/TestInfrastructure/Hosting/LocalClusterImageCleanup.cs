using System.Diagnostics;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Features.TestInfrastructure.Processes;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Releases only the exact locally built image after the selected CLUSTER child has stopped.</summary>
internal sealed class LocalClusterImageCleanup(LocalClusterImageExecution execution, string scriptPath,
    IOptions<TestExecutionOptions> options, TimeProvider? provider = null)
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const string ImageAndProcessCleanupFailureMessage = "Local CLUSTER image cleanup and process settlement failed.";

    private readonly TestExecutionOptions policy = options.Value;

    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        const string FileNameText = "node";
        const string ItemText = "cleanup";
        const string MessageText = "Local CLUSTER image cleanup process could not start.";
        const int EmptyValue = 0;
        const string CleanupAsyncMessageText = "Local CLUSTER image cleanup failed.";
        const int SingleFailureCount = 1;

        using var timeout = new AppHostDeadline(policy.ImageCleanupTimeout, timeProvider, cancellationToken);
        var start = new ProcessStartInfo(FileNameText)
        {
            WorkingDirectory = execution.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add(ItemText);
        start.ArgumentList.Add(execution.Tag);
        start.ArgumentList.Add(execution.ReceiptPath);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException(MessageText);

        var output = LocalClusterOwnedProcessLifetime.ReadBoundedAsync(process.StandardOutput, policy.CleanupOutputCharacters);
        var error = LocalClusterOwnedProcessLifetime.ReadBoundedAsync(process.StandardError, policy.CleanupOutputCharacters);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        try
        {
            await LocalClusterOwnedProcessLifetime.ObserveAsync(exit, output, error, cancellationToken: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
            if (process.ExitCode != EmptyValue)
            {
                throw new InvalidOperationException(CleanupAsyncMessageText);
            }
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await LocalClusterOwnedProcessLifetime.TerminateAndJoinAsync(process, exit, output, error, failures, options, timeProvider)
                .ConfigureAwait(false);
            if (failures.Count == SingleFailureCount)
            {
                throw;
            }
            throw new AggregateException(ImageAndProcessCleanupFailureMessage, failures);
        }
    }
}

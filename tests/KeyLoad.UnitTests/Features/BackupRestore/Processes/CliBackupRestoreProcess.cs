using System.Diagnostics;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed record CliBackupRestoreProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OriginalExitJoined,
    bool StandardOutputJoined,
    bool StandardErrorJoined,
    bool ProcessDisposed);

internal static class CliBackupRestoreProcess
{
    private const string DotnetCommand = "dotnet";
    private const string SolutionFileName = "KeyLoad.slnx";
    private const string CliProjectPath = "src/KeyLoad.Cli/KeyLoad.Cli.csproj";
    private const string CliAssemblyPath = "src/KeyLoad.Cli/bin/Release/net10.0/KeyLoad.Cli.dll";
    private const string StartFailureMessage = "The Release KeyLoad CLI process did not start.";
    private const string UnsettledMessage = "The original Release KeyLoad CLI process did not settle within its cleanup threshold.";

    internal static IOptions<TestExecutionOptions> CaptureExecutionOptions()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        return AppHostOptionsRegistration.BindTestExecution(configuration);
    }

    internal static async Task<CliBackupRestoreProcessResult> RunAsync(
        IOptions<TestExecutionOptions> executionOptions,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        var options = executionOptions.Value;
        using var process = new Process { StartInfo = CreateStartInfo(arguments, environmentOverrides) };
        var failures = new List<Exception>();
        var started = false;
        CliBackupRestoreProcessResult? result = null;
        ServerFailureObserver.Observe(() =>
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException(StartFailureMessage);
            }
        }, failures);
        if (started)
        {
            result = await RunStartedAsync(process, options, failures, cancellationToken).ConfigureAwait(false);
        }
        var disposed = false;
        ServerFailureObserver.Observe(() =>
        {
            process.Dispose();
            disposed = true;
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result is null
            ? throw new InvalidOperationException(StartFailureMessage)
            : result with { ProcessDisposed = disposed };
    }

    private static async Task<CliBackupRestoreProcessResult?> RunStartedAsync(
        Process process, TestExecutionOptions options, List<Exception> failures, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.OrdinaryTimeout);
        var stdout = new CliBackupRestoreProcessOutput(options.CleanupOutputCharacters);
        var stderr = new CliBackupRestoreProcessOutput(options.CleanupOutputCharacters);
        Task? exitTask = null;
        Task? stdoutTask = null;
        Task? stderrTask = null;
        ServerFailureObserver.Observe(() => exitTask = process.WaitForExitAsync(), failures);
        ServerFailureObserver.Observe(() => stdoutTask = stdout.DrainAsync(process.StandardOutput), failures);
        ServerFailureObserver.Observe(() => stderrTask = stderr.DrainAsync(process.StandardError), failures);
        var joined = Task.WhenAll(exitTask ?? Task.CompletedTask,
            stdoutTask ?? Task.CompletedTask, stderrTask ?? Task.CompletedTask);
        await ObserveUntilDeadlineAsync(joined, failures, deadline.Token).ConfigureAwait(false);
        await SettleOriginalTasksAsync(process, joined, options, failures).ConfigureAwait(false);
        return BuildResult(process, stdout, stderr, exitTask, stdoutTask, stderrTask, joined);
    }

    private static async Task ObserveUntilDeadlineAsync(Task joined, List<Exception> failures, CancellationToken token)
    {
        var deadlineSignal = Task.Delay(Timeout.InfiniteTimeSpan, token);
        _ = await Task.WhenAny(joined, deadlineSignal).ConfigureAwait(false);
        var completed = joined.IsCompleted ? joined : deadlineSignal;
        await ServerFailureObserver.ObserveAsync(() => completed, failures).ConfigureAwait(false);
    }

    private static async Task SettleOriginalTasksAsync(Process process, Task joined,
        TestExecutionOptions options, List<Exception> failures)
    {
        if (!joined.IsCompleted)
        {
            ServerFailureObserver.Observe(() => KillIfRunning(process), failures);
            var cleanup = Task.Delay(options.ProcessSettlementTimeout);
            _ = await Task.WhenAny(joined, cleanup).ConfigureAwait(false);
            if (!joined.IsCompleted)
            {
                failures.Add(new TimeoutException(UnsettledMessage));
                ServerFailureObserver.Observe(() => KillIfRunning(process), failures);
            }
            await ServerFailureObserver.ObserveAsync(() => joined, failures).ConfigureAwait(false);
        }
    }

    private static CliBackupRestoreProcessResult? BuildResult(Process process,
        CliBackupRestoreProcessOutput stdout, CliBackupRestoreProcessOutput stderr,
        Task? exitTask, Task? stdoutTask, Task? stderrTask, Task joined)
    {
        if (!joined.IsCompletedSuccessfully || exitTask is null || stdoutTask is null || stderrTask is null)
        {
            return null;
        }
        return new(process.ExitCode, stdout.Text, stderr.Text, exitTask.IsCompleted,
            stdoutTask.IsCompleted, stderrTask.IsCompleted, ProcessDisposed: false);
    }

    private static ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        var root = FindRepositoryRoot();
        var assembly = Path.Combine(root, CliAssemblyPath);
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException(StartFailureMessage, assembly);
        }
        var startInfo = new ProcessStartInfo(DotnetCommand)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environmentOverrides is not null)
        {
            foreach (var entry in environmentOverrides)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }
        return startInfo;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName))
                && File.Exists(Path.Combine(directory.FullName, CliProjectPath)))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(StartFailureMessage);
    }

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
            // The original child exited between the ownership check and kill.
        }
    }
}

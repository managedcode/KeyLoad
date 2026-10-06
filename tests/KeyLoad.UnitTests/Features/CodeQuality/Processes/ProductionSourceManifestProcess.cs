using System.Diagnostics;
using System.Text;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal sealed record ProductionSourceManifestProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OriginalExitJoined,
    bool StandardOutputJoined,
    bool StandardErrorJoined,
    bool ProcessDisposed);

internal static class ProductionSourceManifestProcess
{
    private const string PowerShellCommand = "pwsh";
    private const string ScriptRelativePath = "scripts/Features/CodeQuality/functional-coverage.production-source-manifest.ps1";
    private const string Failure = "The native source-manifest producer process failed.";
    private const string DeadlineFailure = "The native source-manifest producer exceeded its operation deadline.";
    private const int FirstCapturedStreamIndex = 0;
    private const int SecondCapturedStreamIndex = 1;

    internal static string RepositoryRoot => FindRepositoryRoot();

    internal static IOptions<TestExecutionOptions> CaptureExecutionOptions()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        return AppHostOptionsRegistration.BindTestExecution(configuration);
    }

    internal static async Task<ProductionSourceManifestProcessResult> RunAsync(
        IOptions<TestExecutionOptions> executionOptions, string mode, string evidenceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(evidenceRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var options = executionOptions.Value;
        using var process = new Process { StartInfo = CreateStartInfo(mode, evidenceRoot) };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.OrdinaryTimeout);
        var failures = new List<Exception>();
        var started = false;
        ProductionSourceManifestProcessResult? result = null;
        ServerFailureObserver.Observe(() =>
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException(Failure);
            }
        }, failures);
        if (started)
        {
            result = await RunStartedAsync(process, options, failures, deadline.Token, cancellationToken)
                .ConfigureAwait(false);
        }
        var disposed = false;
        ServerFailureObserver.Observe(() =>
        {
            process.Dispose();
            disposed = true;
        }, failures);
        await ServerFailureObserver.ObserveAsync(deadline.CancelAsync, failures).ConfigureAwait(false);
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
        return result is null
            ? throw new InvalidOperationException(Failure)
            : result with { ProcessDisposed = disposed };
    }

    private static async Task<ProductionSourceManifestProcessResult?> RunStartedAsync(
        Process process, TestExecutionOptions options, List<Exception> failures, CancellationToken deadline,
        CancellationToken caller)
    {
        Task? exit = null;
        Task<string>? output = null;
        Task<string>? error = null;
        ServerFailureObserver.Observe(() => exit = process.WaitForExitAsync(CancellationToken.None), failures);
        ServerFailureObserver.Observe(() => output = ReadBoundedAsync(process.StandardOutput, options.CleanupOutputCharacters), failures);
        ServerFailureObserver.Observe(() => error = ReadBoundedAsync(process.StandardError, options.CleanupOutputCharacters), failures);
        var original = Task.WhenAll(exit ?? Task.CompletedTask, output ?? Task.CompletedTask,
            error ?? Task.CompletedTask);
        await ObserveUntilDeadlineAsync(original, failures, deadline, caller).ConfigureAwait(false);
        await ProductionSourceManifestProcessSettlement.JoinAsync(process, original, options.ProcessSettlementTimeout,
            failures).ConfigureAwait(false);
        if (!original.IsCompletedSuccessfully || exit is null || output is null || error is null || failures.Count != 0)
        {
            return null;
        }
        var captured = await Task.WhenAll(output, error).ConfigureAwait(false);
        return new(process.ExitCode, captured[FirstCapturedStreamIndex], captured[SecondCapturedStreamIndex],
            exit.IsCompletedSuccessfully, output.IsCompletedSuccessfully, error.IsCompletedSuccessfully, false);
    }

    private static async Task ObserveUntilDeadlineAsync(Task original, List<Exception> failures,
        CancellationToken deadline, CancellationToken caller)
    {
        var signal = Task.Delay(Timeout.InfiniteTimeSpan, deadline);
        if (await Task.WhenAny(original, signal).ConfigureAwait(false) == original)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
            return;
        }
        failures.Add(caller.IsCancellationRequested
            ? new OperationCanceledException(caller)
            : new TimeoutException(DeadlineFailure));
    }

    private static ProcessStartInfo CreateStartInfo(string mode, string evidenceRoot)
    {
        var root = RepositoryRoot;
        var start = new ProcessStartInfo(PowerShellCommand)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root, ScriptRelativePath));
        start.ArgumentList.Add("-Mode");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("-Root");
        start.ArgumentList.Add(root);
        start.ArgumentList.Add("-EvidenceRoot");
        start.ArgumentList.Add(evidenceRoot);
        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var output = new StringBuilder();
        var buffer = new char[maximumCharacters];
        var oversized = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                if (oversized)
                {
                    throw new InvalidOperationException(Failure);
                }
                return output.ToString();
            }
            if (output.Length > maximumCharacters - count)
            {
                oversized = true;
            }
            else if (!oversized)
            {
                output.Append(buffer, 0, count);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, ScriptRelativePath)))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(Failure);
    }
}

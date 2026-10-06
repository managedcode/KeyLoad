using System.Diagnostics;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageNodeResult(int ExitCode, string StandardOutput,
    string StandardError, bool OriginalExitJoined, bool StandardOutputJoined,
    bool StandardErrorJoined, bool ProcessDisposed);

internal static class NativeCoverageImageNodeProcess
{
    private const string NodeCommand = "node";
    private const string EntryRelativePath = "scripts/Features/CodeQuality/functional-coverage.server-image.mjs";
    private const string InvocationOption = "--invocation=";
    private const string DescriptorLimitOption = "--maximum-descriptor-bytes=";
    private const string StartFailure = "The native coverage image materializer did not start.";

    internal static async Task<NativeCoverageImageNodeResult> RunAsync(string repositoryRoot,
        NativeCoverageImageInvocation invocation, IOptions<TestExecutionOptions> executionOptions,
        IOptions<NativeCoverageExecutionOptions> coverageOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(coverageOptions);
        var execution = executionOptions.Value;
        var coverage = coverageOptions.Value;
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(execution.OrdinaryTimeout);
        using var process = new Process { StartInfo = CreateStartInfo(repositoryRoot, invocation, coverage) };
        var failures = new List<Exception>();
        var started = false;
        ServerFailureObserver.Observe(() =>
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException(StartFailure);
            }
        }, failures);
        var joined = started
            ? await RunStartedAsync(process, execution, failures, deadline.Token).ConfigureAwait(false)
            : NativeCoverageImageNodeJoined.Empty;
        var disposed = false;
        ServerFailureObserver.Observe(() =>
        {
            process.Dispose();
            disposed = true;
        }, failures);
        await ServerFailureObserver.ObserveAsync(deadline.CancelAsync, failures).ConfigureAwait(false);
        NativeCoverageImageNodeSettlement.ThrowFailures(failures);
        return new(joined.ExitCode, joined.StandardOutput, joined.StandardError,
            joined.ExitJoined, joined.OutputJoined, joined.ErrorJoined, disposed);
    }

    private static async Task<NativeCoverageImageNodeJoined> RunStartedAsync(Process process,
        TestExecutionOptions execution, List<Exception> failures, CancellationToken deadline)
    {
        Task? exit = null;
        Task<string>? output = null;
        Task<string>? error = null;
        ServerFailureObserver.Observe(() => exit = process.WaitForExitAsync(CancellationToken.None), failures);
        ServerFailureObserver.Observe(() => output = NativeCoverageImageNodeOutput.ReadBoundedAsync(process.StandardOutput,
            execution.CleanupOutputCharacters), failures);
        ServerFailureObserver.Observe(() => error = NativeCoverageImageNodeOutput.ReadBoundedAsync(process.StandardError,
            execution.CleanupOutputCharacters), failures);
        var original = Task.WhenAll(exit ?? Task.CompletedTask, output ?? Task.CompletedTask,
            error ?? Task.CompletedTask);
        await ServerFailureObserver.ObserveAsync(() => original.WaitAsync(deadline), failures).ConfigureAwait(false);
        return await NativeCoverageImageNodeSettlement.SettleAsync(process, exit, output, error,
            execution.ProcessSettlementTimeout, failures).ConfigureAwait(false);
    }

    private static ProcessStartInfo CreateStartInfo(string repositoryRoot, NativeCoverageImageInvocation invocation,
        NativeCoverageExecutionOptions options)
    {
        var start = new ProcessStartInfo(NodeCommand)
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(repositoryRoot, EntryRelativePath));
        start.ArgumentList.Add(InvocationOption + invocation.Path);
        start.ArgumentList.Add(DescriptorLimitOption
            + options.MaximumDescriptorBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return start;
    }
}

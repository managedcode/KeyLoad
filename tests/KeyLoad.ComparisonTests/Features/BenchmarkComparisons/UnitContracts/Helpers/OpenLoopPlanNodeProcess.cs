using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanNodeProcess
{
    private const string NodeCommand = "node";
    private const string InputTypeArgument = "--input-type=module";
    private const string EvalArgument = "-e";
    private const string PathEnvironment = "PATH";
    private const string FailureMessage = "The open-loop plan Node process failed.";
    private const string InputLimitMessage = "The open-loop plan process input exceeded its bound.";
    private const string ModuleDirectory = "scripts/Features/BenchmarkComparisons";
    private const string ContractFile = "benchmarks/KeyLoad.Comparisons/Features/BenchmarkComparisons/isolated-contract.json";

    internal static string RepositoryRoot => FindRepositoryRoot();

    internal static string ContractPath => Path.Combine(RepositoryRoot, ContractFile);

    internal static async Task<JsonDocument> ReadContractAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(ContractPath);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static Task<OpenLoopPlanNodeResult> RunAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions,
        string operation, string? input, string? outputPath, bool keepStandardInputOpen,
        TaskCompletionSource? ready, CancellationToken cancellationToken)
        => RunOwnedAsync(executionOptions, CreateStartInfo(operation, outputPath), input, keepStandardInputOpen, ready,
            cancellationToken);

    internal static Task<OpenLoopPlanNodeResult> RunOwnedAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, ProcessStartInfo startInfo, string? input,
        bool keepStandardInputOpen, TaskCompletionSource? ready, CancellationToken cancellationToken)
        => RunCoreAsync(executionOptions, startInfo, input, keepStandardInputOpen, ready,
            observeSettlement: null, cancellationToken);

    internal static Task<OpenLoopPlanNodeResult> RunObservedAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, ProcessStartInfo startInfo,
        OpenLoopCohortStageEvidence evidence, OpenLoopCohortStage stage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return RunCoreAsync(executionOptions, startInfo, input: null, keepStandardInputOpen: false, ready: null,
            actual => evidence.RecordAsync(stage, actual), cancellationToken);
    }

    private static async Task<OpenLoopPlanNodeResult> RunCoreAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, ProcessStartInfo startInfo, string? input,
        bool keepStandardInputOpen, TaskCompletionSource? ready,
        Func<OpenLoopPlanStageObservation, Task>? observeSettlement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(startInfo);
        var processOptions = executionOptions.Value;
        processOptions.Validate();
        if (input is not null && Encoding.UTF8.GetByteCount(input) > processOptions.MaximumInputBytes)
        {
            throw new InvalidOperationException(InputLimitMessage);
        }
        using var process = new Process { StartInfo = startInfo };
        using var deadlineTimeout = new CancellationTokenSource(processOptions.ProcessTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var failures = new List<Exception>();
        var clock = new TestElapsedClock(TimeProvider.System);
        var outputCapture = new OpenLoopPlanOutputCapture(executionOptions);
        var errorCapture = new OpenLoopPlanOutputCapture(executionOptions);
        var started = StartProcess(process, failures);
        Task<string>? output = null;
        Task<string>? error = null;
        if (started)
        {
            output = outputCapture.ReadAsync(process.StandardOutput, ready);
            error = errorCapture.ReadAsync(process.StandardError, ready: null);
            await ServerFailureObserver.ObserveAsync(
                () => WriteAndWaitAsync(process, input, keepStandardInputOpen, deadline.Token), failures).ConfigureAwait(false);
        }
        var executionMilliseconds = clock.Elapsed.TotalMilliseconds;
        var callerCancelledAtExecutionCompletion = cancellationToken.IsCancellationRequested;
        var deadlineCancelledAtExecutionCompletion = deadline.IsCancellationRequested;
        await OpenLoopPlanProcessSettlement.ObserveAsync(process, started, output, error, failures)
            .ConfigureAwait(false);
        var settlementMilliseconds = clock.Elapsed.TotalMilliseconds - executionMilliseconds;
        await OpenLoopPlanProcessSettlement.RecordObservationAsync(process, started, output, error,
            executionMilliseconds, settlementMilliseconds, callerCancelledAtExecutionCompletion,
            deadlineCancelledAtExecutionCompletion, outputCapture, errorCapture, observeSettlement, failures)
            .ConfigureAwait(false);
        var result = await OpenLoopPlanProcessSettlement.CaptureResultAsync(process, started, output, error, failures)
            .ConfigureAwait(false);
        ServerFailureObserver.Observe(process.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(FailureMessage);
    }

    private static bool StartProcess(Process process, List<Exception> failures)
    {
        var started = false;
        ServerFailureObserver.Observe(() =>
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException(FailureMessage);
            }
        }, failures);
        return started;
    }

    private static async Task WriteAndWaitAsync(Process process, string? input, bool keepStandardInputOpen,
        CancellationToken cancellationToken)
    {
        if (input is not null)
        {
            await process.StandardInput.WriteLineAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        if (!keepStandardInputOpen)
        {
            process.StandardInput.Close();
        }
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProcessStartInfo CreateStartInfo(string operation, string? outputPath)
    {
        var root = RepositoryRoot;
        var module = Path.Combine(root, ModuleDirectory, "open-loop-isolated-plan.mjs");
        var start = new ProcessStartInfo(NodeCommand)
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(InputTypeArgument);
        start.ArgumentList.Add(EvalArgument);
        start.ArgumentList.Add(OpenLoopPlanNodeProgram.Source);
        var path = Environment.GetEnvironmentVariable(PathEnvironment);
        start.Environment.Clear();
        if (!string.IsNullOrWhiteSpace(path))
        {
            start.Environment[PathEnvironment] = path;
        }
        start.Environment[OpenLoopPlanNodeProgram.ModuleEnvironment] = module;
        start.Environment[OpenLoopPlanNodeProgram.OperationEnvironment] = operation;
        if (outputPath is not null)
        {
            start.Environment[OpenLoopPlanNodeProgram.OutputEnvironment] = outputPath;
        }
        return start;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx"))
                && File.Exists(Path.Combine(directory.FullName, ModuleDirectory, "isolated-plan.mjs")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(FailureMessage);
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KeyLoad.Server;
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

    internal static async Task<OpenLoopPlanNodeResult> RunOwnedAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, ProcessStartInfo startInfo, string? input,
        bool keepStandardInputOpen, TaskCompletionSource? ready, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(startInfo);
        var processOptions = executionOptions.Value;
        processOptions.Validate();
        if (input is not null && Encoding.UTF8.GetByteCount(input) > processOptions.MaximumInputBytes)
        {
            throw new InvalidOperationException(InputLimitMessage);
        }
        var process = new Process { StartInfo = startInfo };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(processOptions.ProcessTimeout);
        var failures = new List<Exception>();
        var started = false;
        Task<string>? output = null;
        Task<string>? error = null;
        OpenLoopPlanNodeResult? result = null;
        ServerFailureObserver.Observe(() =>
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException(FailureMessage);
            }
        }, failures);
        if (started)
        {
            output = ReadBoundedAsync(process.StandardOutput, processOptions, ready);
            error = ReadBoundedAsync(process.StandardError, processOptions, ready: null);
            await ServerFailureObserver.ObserveAsync(
                () => WriteAndWaitAsync(process, input, keepStandardInputOpen, deadline.Token), failures).ConfigureAwait(false);
        }
        await OpenLoopPlanProcessSettlement.ObserveAsync(process, started, output, error, failures)
            .ConfigureAwait(false);
        if (started && failures.Count == 0)
        {
            async Task CaptureResultAsync()
            {
                var captured = await Task.WhenAll(output!, error!).ConfigureAwait(false);
                result = new(process.ExitCode, captured[0], captured[1]);
            }
            await ServerFailureObserver.ObserveAsync(CaptureResultAsync, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.Observe(process.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(FailureMessage);
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

    private static async Task<string> ReadBoundedAsync(StreamReader reader, OpenLoopPlanProcessOptions processOptions,
        TaskCompletionSource? ready)
    {
        var output = new StringBuilder();
        var buffer = new char[processOptions.StreamBufferCharacters];
        var readyObserved = false;
        var exceededBound = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            {
                if (exceededBound)
                {
                    throw new InvalidOperationException(FailureMessage);
                }
                return output.ToString();
            }
            if (output.Length + count > processOptions.MaximumOutputCharacters)
            {
                exceededBound = true;
            }
            else if (!exceededBound)
            {
                output.Append(buffer, 0, count);
            }
            if (ready is not null && !readyObserved
                && output.ToString().Contains(OpenLoopPlanNodeProgram.ReadyMarker, StringComparison.Ordinal))
            {
                readyObserved = true;
                ready.TrySetResult();
            }
        }
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

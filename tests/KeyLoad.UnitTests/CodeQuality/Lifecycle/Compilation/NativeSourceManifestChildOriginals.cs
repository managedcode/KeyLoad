using System.Diagnostics;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal sealed class NativeSourceManifestChildOriginals
{
    private const string WitnessPrefix = "native-source-manifest-child-settlement";
    internal bool Started
    {
        get; set;
    }
    internal Task? Exit
    {
        get; private set;
    }
    internal Task<string?>? Ready
    {
        get; private set;
    }
    internal Task<string>? Output
    {
        get; private set;
    }
    internal Task<string>? Error
    {
        get; private set;
    }
    internal Task? Original
    {
        get; private set;
    }

    internal void Capture(Process process, TestExecutionOptions options, List<Exception> failures)
    {
        ServerFailureObserver.Observe(() => Exit ??= process.WaitForExitAsync(CancellationToken.None), failures);
        ServerFailureObserver.Observe(() => Ready ??= process.StandardOutput.ReadLineAsync(CancellationToken.None)
            .AsTask(), failures);
        ServerFailureObserver.Observe(() => Output ??= Ready is null
            ? NativeCoverageImageNodeOutput.ReadBoundedAsync(process.StandardOutput, options.CleanupOutputCharacters)
            : NativeSourceManifestChildSettlement.CaptureOutputAsync(Ready, process.StandardOutput,
                options.CleanupOutputCharacters), failures);
        ServerFailureObserver.Observe(() => Error ??= NativeCoverageImageNodeOutput.ReadBoundedAsync(
            process.StandardError, options.CleanupOutputCharacters), failures);
        Original ??= Task.WhenAll(ActualStages());
    }

    internal async Task SettleAsync(Process process, TestExecutionOptions options, TimeProvider timeProvider,
        List<Exception> failures)
    {
        if (!Started)
        {
            return;
        }
        var original = Original ?? throw new InvalidOperationException(NativeSourceManifestChildSettlement.StartFailure);
        await using var cleanup = await ProductionSourceManifestProcessSettlement.JoinAsync(process, Started, original,
            options.ProcessSettlementTimeout, timeProvider, options.TerminationGrace, failures).ConfigureAwait(false);
        await cleanup.ConfirmNativeExitAsync(process, options.ProcessExitPollInterval).ConfigureAwait(false);
    }

    internal async Task<NativeSourceManifestChildJoined?> JoinedAsync(Process process, bool pending,
        List<Exception> failures, CancellationToken caller)
    {
        if (Original is null || Exit is null || Ready is null || Output is null || Error is null)
        {
            return null;
        }
        await ObserveSettlementFailuresAsync(Original, Exit, Ready, Output, Error, failures).ConfigureAwait(false);
        return new(Original, Exit, Output, Error, Ready.IsCompletedSuccessfully ? await Ready.ConfigureAwait(false) ?? string.Empty : string.Empty,
            Output.IsCompletedSuccessfully ? await Output.ConfigureAwait(false) : string.Empty,
            Error.IsCompletedSuccessfully ? await Error.ConfigureAwait(false) : string.Empty,
            pending, process.HasExited, false, false, failures, caller);
    }

    private static async Task ObserveSettlementFailuresAsync(Task original, Task exit, Task<string?> ready,
        Task<string> output, Task<string> error, List<Exception> failures)
    {
        if (original.IsCompletedSuccessfully && exit.IsCompletedSuccessfully && ready.IsCompletedSuccessfully &&
            output.IsCompletedSuccessfully && error.IsCompletedSuccessfully)
        {
            return;
        }
        Task[] terminals = [original, exit, ready, output, error];
        await ObserveTerminalFailuresAsync(terminals, failures).ConfigureAwait(false);
        await WriteWitnessAsync(original, exit, ready, output, error, failures).ConfigureAwait(false);
    }

    private static async Task ObserveTerminalFailuresAsync(Task[] terminals, List<Exception> failures)
    {
        var observed = new List<Exception>();
        foreach (var terminal in terminals)
        {
            if (terminal.IsCompleted && !terminal.IsCompletedSuccessfully)
            {
                await ServerFailureObserver.ObserveAsync(() => terminal, observed).ConfigureAwait(false);
            }
        }
        foreach (var failure in observed)
        {
            if (!failures.Any(retained => ReferenceEquals(retained, failure)))
            {
                failures.Add(failure);
            }
        }
    }

    private static Task WriteWitnessAsync(Task original, Task exit, Task<string?> ready,
        Task<string> output, Task<string> error, List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(() => Console.Out.WriteLineAsync(
                $"{WitnessPrefix} originalCompleted={original.IsCompleted} originalSucceeded={original.IsCompletedSuccessfully} " +
                $"originalFaulted={original.IsFaulted} originalCanceled={original.IsCanceled} exitCompleted={exit.IsCompleted} " +
                $"readySucceeded={ready.IsCompletedSuccessfully} outputCompleted={output.IsCompleted} " +
                $"outputSucceeded={output.IsCompletedSuccessfully} outputFaulted={output.IsFaulted} outputCanceled={output.IsCanceled} " +
                $"errorCompleted={error.IsCompleted} errorSucceeded={error.IsCompletedSuccessfully} " +
                $"originalExceptionCount={original.Exception?.InnerExceptions.Count ?? 0} " +
                $"outputExceptionCount={output.Exception?.InnerExceptions.Count ?? 0} retainedFailureCount={failures.Count}"),
            failures);

    private IEnumerable<Task> ActualStages()
    {
        if (Exit is not null)
        {
            yield return Exit;
        }
        if (Output is not null)
        {
            yield return Output;
        }
        else if (Ready is not null)
        {
            yield return Ready;
        }
        if (Error is not null)
        {
            yield return Error;
        }
    }

}

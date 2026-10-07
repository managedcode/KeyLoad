using System.Diagnostics;
using System.Runtime.ExceptionServices;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal sealed class CommandIdempotencyProcessChild
{
    private readonly int outputLimitCharacters;
    private readonly string? expectedSignal;
    private Process? process;
    private Task? processExit;
    private CommandIdempotencyProcessPipes? pipes;
    private Exception? startupFailure;
    internal bool IsSettled { get; private set; }
    internal bool IsDisposed { get; private set; }

    private CommandIdempotencyProcessChild(int outputLimitCharacters, string? expectedSignal)
    {
        this.outputLimitCharacters = outputLimitCharacters;
        this.expectedSignal = expectedSignal;
    }

    internal int ExitCode => RequireProcess().ExitCode;
    internal static CommandIdempotencyProcessChild Create(int outputLimitCharacters, bool expectAcknowledgement)
        => Create(outputLimitCharacters, expectAcknowledgement ? CrashFixtureValues.Acknowledgement : null);

    internal static CommandIdempotencyProcessChild Create(int outputLimitCharacters, string? expectedSignal)
        => new(outputLimitCharacters, expectedSignal);

    internal void Start(ProcessStartInfo start)
    {
        Process? launched = null;
        try
        {
            launched = Process.Start(start);
            if (launched is null)
            {
                startupFailure = new InvalidOperationException("Could not start the document command CrashHost.");
            }
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            startupFailure = failure;
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            startupFailure = failure;
        }
        if (launched is null)
        {
            IsSettled = true;
            return;
        }
        Attach(launched);
    }

    private void Attach(Process startedProcess)
    {
        process = startedProcess;
        ObserveExit(startedProcess);
        try
        {
            pipes = CommandIdempotencyProcessPipes.Create(startedProcess, outputLimitCharacters,
                expectedSignal);
            pipes.StartReaders();
            startupFailure = CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(
                startupFailure, pipes.StartupFailure);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            startupFailure = CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(startupFailure, failure);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            startupFailure = CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(startupFailure, failure);
        }
    }

    private void ObserveExit(Process startedProcess)
    {
        try
        {
            processExit = startedProcess.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            startupFailure = CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(startupFailure, failure);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            startupFailure = CommandIdempotencyProcessFailureHandling.PreserveStartupFailure(startupFailure, failure);
        }
    }

    internal void ThrowStartupFailure()
    {
        if (startupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(startupFailure).Throw();
        }
    }

    internal Task WaitForAcknowledgementAsync(CancellationToken cancellationToken)
        => RequirePipes().WaitForAcknowledgementAsync(cancellationToken);

    internal async Task WaitAndJoinAsync(CancellationToken cancellationToken)
    {
        var originalExit = RequireExitTask();
        var activePipes = RequirePipes();
        var completed = await Task.WhenAny(originalExit, activePipes.FailureTask).WaitAsync(cancellationToken);
        if (ReferenceEquals(completed, activePipes.FailureTask))
        {
            throw await activePipes.FailureTask;
        }
        await originalExit.WaitAsync(cancellationToken);
        await JoinReadersAsync(cancellationToken);
    }

    internal async Task StopAndJoinAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var activeProcess = RequireProcess();
        CommandIdempotencyProcessNativeExit.TryKill(activeProcess, failures);
        await CommandIdempotencyProcessNativeExit.ObserveExitAsync(processExit, failures, cancellationToken);
        await JoinReadersForCleanupAsync(failures, cancellationToken);
        var processExited = CommandIdempotencyProcessNativeExit.HasExited(activeProcess, failures);
        IsSettled = (processExit is null || processExit.IsCompleted) && processExited
            && (pipes is null || pipes.ReadersSettled);
        CommandIdempotencyProcessFailureHandling.ThrowFailures(failures);
    }

    internal void CloseNativeProcessAfterJoin()
    {
        if (IsDisposed)
        {
            return;
        }
        if (!IsSettled || process is null && (processExit is not null || pipes is not null)
            || process is not null && (processExit is { IsCompleted: false }
                || pipes is not null && !pipes.ReadersSettled))
        {
            throw new InvalidOperationException("The document command child cannot be disposed before process and pipe settlement.");
        }
        process?.Dispose();
        IsDisposed = true;
    }

    private async Task JoinReadersForCleanupAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        if (pipes is null)
        {
            return;
        }
        try
        {
            await pipes.JoinAsync(cancellationToken);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
        }
    }

    private async Task JoinReadersAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await JoinReadersForCleanupAsync(failures, cancellationToken);
        var activeProcess = RequireProcess();
        var processExited = CommandIdempotencyProcessNativeExit.HasExited(activeProcess, failures);
        IsSettled = (processExit is null || processExit.IsCompleted) && processExited
            && (pipes is null || pipes.ReadersSettled);
        CommandIdempotencyProcessFailureHandling.ThrowFailures(failures);
    }

    private Process RequireProcess()
        => process ?? throw new InvalidOperationException("The document command process was not attached.");

    private Task RequireExitTask()
        => processExit ?? throw new InvalidOperationException("The original process exit observer did not start.");

    private CommandIdempotencyProcessPipes RequirePipes()
        => pipes ?? throw new InvalidOperationException("The document command pipe readers did not start.");
}

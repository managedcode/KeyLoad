using System.Diagnostics;
using KeyLoad.Server;
using KeyLoad.Storage.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal sealed class C1OutcomeInspectionProcessLifetime : IAsyncDisposable
{
    private const string StartFailureMessage = "The outcome inspection process did not start.";
    private const string UnreapedProcessMessage = "The outcome inspection child did not exit after its original tasks settled.";
    private const string ProcessHandleCloseMessage = "The outcome inspection process handle remained open after disposal.";
    private const string OwnerReleaseMessage = "The outer owner lock remained open after child settlement.";
    private readonly ReadOnlyMemory<byte> input;
    private readonly string ownerPath;
    private readonly List<Exception> failures;
    private readonly C1OutcomeInspectionCapture stdout;
    private readonly C1OutcomeInspectionCapture stderr;
    private readonly IOptions<CrashHostExecutionOptions> executionOptions;
    private readonly CrashHostExecutionOptions settings;
    private readonly Process process = new();
    private FileStream? owner;
    private Task? exitTask;
    private Task? inputTask;
    private Task? stdoutTask;
    private Task? stderrTask;
    private int processId;
    private int exitCode;
    private bool started;
    private bool reaped;
    private bool processClosed;
    private bool ownerReleased;
    private bool originalTasksJoined;
    private bool unreapedProcessReported;
    private bool processHandleCloseReported;
    private bool ownerReleaseReported;

    internal C1OutcomeInspectionProcessLifetime(ReadOnlyMemory<byte> input, string ownerPath,
        List<Exception> failures, IOptions<CrashHostExecutionOptions> executionOptions)
    {
        this.input = input;
        this.ownerPath = ownerPath;
        this.failures = failures;
        this.executionOptions = executionOptions;
        settings = executionOptions.Value;
        stdout = new(executionOptions);
        stderr = new(executionOptions);
    }

    internal bool DisposalAttemptCompleted { get; private set; }

    internal C1OutcomeInspectionProcessResult Result => new(processId, exitCode, stdout.Bytes, stderr.Bytes,
        stdout.Exceeded, stderr.Exceeded, reaped, inputTask?.IsCompleted ?? true,
        stdoutTask?.IsCompleted ?? true, stderrTask?.IsCompleted ?? true, processClosed, ownerReleased);

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        ServerFailureObserver.Observe(Start, failures);
        if (started)
        { await ServerFailureObserver.ObserveAsync(() => SettleAsync(cancellationToken), failures).ConfigureAwait(false); }
    }

    private void Start()
    {
        owner = OfflineRegularFile.Open(ownerPath, FileAccess.ReadWrite, FileShare.None,
            settings.InspectionReadBufferBytes);
        process.StartInfo = C1OutcomeInspectionProcessStartInfo.Create();
        if (!process.Start())
        { throw new InvalidOperationException(StartFailureMessage); }
        started = true;
        processId = process.Id;
        exitTask = process.WaitForExitAsync();
        stdoutTask = stdout.DrainAsync(process.StandardOutput.BaseStream);
        stderrTask = stderr.DrainAsync(process.StandardError.BaseStream);
        inputTask = C1OutcomeInspectionProcessIo.WriteInputAsync(process, input);
    }

    private async Task SettleAsync(CancellationToken cancellationToken)
    {
        var all = Task.WhenAll(exitTask!, inputTask!, stdoutTask!, stderrTask!);
        await C1OutcomeInspectionDeadline.WaitAsync(process, all, failures, executionOptions, cancellationToken).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => all, failures).ConfigureAwait(false);
        originalTasksJoined = all.IsCompleted;
        reaped = process.HasExited;
        if (!reaped)
        {
            ServerFailureObserver.Observe(() => C1OutcomeInspectionProcessIo.Kill(process), failures);
            await ServerFailureObserver.ObserveAsync(() => process.WaitForExitAsync(), failures).ConfigureAwait(false);
            reaped = process.HasExited;
        }
        if (!reaped)
        { ReportUnreapedProcess(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (DisposalAttemptCompleted)
        { return; }
        await JoinOriginalTasksBeforeReleaseAsync().ConfigureAwait(false);
        if ((!started || reaped && originalTasksJoined) && !processClosed)
        {
            var handle = started ? process.SafeHandle : null;
            try
            {
                process.Dispose();
                processClosed = handle?.IsClosed ?? true;
            }
            catch (Exception error) when (handle?.IsClosed ?? true)
            {
                processClosed = true;
                failures.Add(error);
            }
        }
        if (started && reaped && originalTasksJoined && !processClosed)
        { ReportProcessHandleCloseFailure(); }
        if (owner is not null && !ownerReleased && (!started || reaped && originalTasksJoined && processClosed))
        {
            var handle = owner.SafeFileHandle;
            try
            {
                owner.Dispose();
                ownerReleased = handle.IsClosed;
            }
            catch (Exception error) when (handle.IsClosed)
            {
                ownerReleased = true;
                failures.Add(error);
            }
        }
        if (owner is not null && !ownerReleased && started && reaped && originalTasksJoined && processClosed)
        { ReportOwnerReleaseFailure(); }
        DisposalAttemptCompleted = (!started || reaped && originalTasksJoined)
            && processClosed && (owner is null || ownerReleased);
    }

    private async Task JoinOriginalTasksBeforeReleaseAsync()
    {
        if (started && !originalTasksJoined)
        {
            ServerFailureObserver.Observe(() => C1OutcomeInspectionProcessIo.Kill(process), failures);
            var outstanding = Task.WhenAll(exitTask ?? Task.CompletedTask, inputTask ?? Task.CompletedTask,
                stdoutTask ?? Task.CompletedTask, stderrTask ?? Task.CompletedTask);
            await ServerFailureObserver.ObserveAsync(() => outstanding, failures).ConfigureAwait(false);
            originalTasksJoined = outstanding.IsCompleted;
            reaped = process.HasExited;
        }
        if (started && originalTasksJoined && !reaped)
        {
            await ServerFailureObserver.ObserveAsync(() => process.WaitForExitAsync(), failures).ConfigureAwait(false);
            reaped = process.HasExited;
            if (!reaped)
            { ReportUnreapedProcess(); }
        }
        if (started && reaped)
        { exitCode = process.ExitCode; }
    }

    private void ReportUnreapedProcess()
    {
        if (unreapedProcessReported)
        { return; }
        unreapedProcessReported = true;
        failures.Add(new InvalidOperationException(UnreapedProcessMessage));
    }

    private void ReportProcessHandleCloseFailure()
    {
        if (processHandleCloseReported)
        { return; }
        processHandleCloseReported = true;
        failures.Add(new InvalidOperationException(ProcessHandleCloseMessage));
    }

    private void ReportOwnerReleaseFailure()
    {
        if (ownerReleaseReported)
        { return; }
        ownerReleaseReported = true;
        failures.Add(new InvalidOperationException(OwnerReleaseMessage));
    }

}

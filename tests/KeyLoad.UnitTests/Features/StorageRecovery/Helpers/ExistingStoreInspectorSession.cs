using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ExistingStoreInspectorSession : IAsyncDisposable
{
    private const int CleanupWarningSeconds = 10;
    private const string ProcessStartFailureMessage = "The existing-store inspector process did not start.";
    private Task? completionTask;
    private Task? exitTask;
    private Task? inputTask;
    private Task? stdoutTask;
    private Task? stderrTask;
    private bool released;

    private ExistingStoreInspectorSession() { }

    internal ExistingStoreInspectorPipeCapture Stdout { get; } = new();
    internal ExistingStoreInspectorPipeCapture Stderr { get; } = new();
    internal Process Process { get; } = new();
    internal Task StdoutTask => stdoutTask!;
    internal Task StderrTask => stderrTask!;
    internal bool OriginalTasksJoined { get; private set; }

    internal static async Task<ExistingStoreInspectorSession> StartAsync(ProcessStartInfo startInfo, string payload)
    {
        var session = new ExistingStoreInspectorSession();
        try
        {
            session.Start(startInfo, payload);
            return session;
        }
        catch (Exception original)
        {
            await ExistingStoreInspectorSessionStartFailure.CleanupAsync(session.Process, original,
                session.exitTask, session.inputTask, session.stdoutTask, session.stderrTask, session.completionTask);
            throw;
        }
    }

    private void Start(ProcessStartInfo startInfo, string payload)
    {
        Process.StartInfo = startInfo;
        Process.EnableRaisingEvents = true;
        if (!Process.Start())
        {
            throw new InvalidOperationException(ProcessStartFailureMessage);
        }
        exitTask = Process.WaitForExitAsync();
        stdoutTask = Stdout.DrainAsync(Process.StandardOutput);
        stderrTask = Stderr.DrainAsync(Process.StandardError);
        inputTask = WriteRequestAsync(Process, payload);
        completionTask = Task.WhenAll(exitTask, inputTask, stdoutTask, stderrTask);
    }

    internal async Task<bool> WaitAsync(bool cancelWhenReady, CancellationToken token)
    {
        if (!cancelWhenReady)
        {
            await completionTask!.WaitAsync(token);
            return false;
        }
        var first = await Task.WhenAny(Stderr.ReadyTask, completionTask!).WaitAsync(token);
        if (first == Stderr.ReadyTask && !completionTask!.IsCompleted && !Process.HasExited)
        {
            return true;
        }
        await completionTask!;
        return false;
    }

    internal async Task<bool> SettleAsync(List<Exception> failures, bool killImmediately = false)
    {
        if (OriginalTasksJoined)
        {
            return false;
        }
        if (killImmediately)
        {
            ExistingStoreInspectorFailureJoin.Capture(Kill, failures);
        }
        var warning = !ReferenceEquals(await Task.WhenAny(completionTask!,
            Task.Delay(TimeSpan.FromSeconds(CleanupWarningSeconds))), completionTask);
        if (warning && !killImmediately)
        {
            ExistingStoreInspectorFailureJoin.Capture(Kill, failures);
        }
        if (!OriginalTasksJoined)
        {
            var exited = await ExistingStoreInspectorSessionStartFailure.ObserveProcessExitAsync(Process, exitTask, failures);
            await ExistingStoreInspectorFailureJoin.ObserveAsync(inputTask!, failures);
            await ExistingStoreInspectorFailureJoin.ObserveAsync(stdoutTask!, failures);
            await ExistingStoreInspectorFailureJoin.ObserveAsync(stderrTask!, failures);
            await ExistingStoreInspectorFailureJoin.ObserveAsync(completionTask!, failures);
            if (!exited)
            {
                await ExistingStoreInspectorSessionStartFailure.RetainUnsettledAsync(Process, failures);
            }
            OriginalTasksJoined = true;
        }
        return warning;
    }

    internal void Release(List<Exception> failures)
    {
        if (OriginalTasksJoined && !released)
        {
            released = true;
            ExistingStoreInspectorFailureJoin.Capture(Process.Dispose, failures);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (released)
        {
            return;
        }
        var failures = new List<Exception>();
        await ExistingStoreInspectorFailureJoin.ObserveAsync(SettleAsync(failures, killImmediately: true), failures);
        if (!OriginalTasksJoined)
        {
            await ExistingStoreInspectorSessionStartFailure.RetainUnsettledAsync(Process, failures);
        }
        Release(failures);
        ExistingStoreInspectorFailureJoin.Throw(failures);
    }

    private void Kill()
    {
        if (!Process.HasExited)
        {
            Process.Kill(entireProcessTree: true);
        }
    }

    private static async Task WriteRequestAsync(Process process, string payload)
    {
        var failures = new List<Exception>();
        await ExistingStoreInspectorFailureJoin.ObserveAsync(WriteAndFlushAsync(process, payload), failures);
        ExistingStoreInspectorFailureJoin.Capture(() => CloseInput(process), failures);
        ExistingStoreInspectorFailureJoin.Throw(failures);
    }

    private static void CloseInput(Process process) => process.StandardInput.Close();

    private static async Task WriteAndFlushAsync(Process process, string payload)
    {
        await process.StandardInput.WriteAsync(payload.AsMemory());
        await process.StandardInput.FlushAsync();
    }
}

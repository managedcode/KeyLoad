using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains original process and pipe tasks until the actual child is wholly settled.</summary>
internal sealed class ControlledPartitionMovementTerminalProcessChild(NativeMovementProcessOptions options,
    string? signal) : IDisposable
{
    private const int SuccessfulExit = 0;
    private Process? process;
    private Task? exit;
    private Task? stdout;
    private Task? stderr;
    private ControlledPartitionMovementTerminalProcessPipe? output;
    private bool started;
    internal bool Settled { get; private set; }

    internal void Start(ProcessStartInfo start)
    {
        process = new() { StartInfo = start };
        if (!process.Start())
        { throw new IOException("The original movement child did not start."); }
        started = true;
        exit = process.WaitForExitAsync(CancellationToken.None);
        output = new(signal, options);
        stdout = output.DrainAsync(process.StandardOutput);
        var error = new ControlledPartitionMovementTerminalProcessPipe(null, options);
        stderr = error.DrainAsync(process.StandardError);
    }

    internal async Task CompleteAsync(CancellationToken token)
    {
        var joined = Task.WhenAll(exit!, stdout!, stderr!);
        if (signal is not null)
        {
            var first = await Task.WhenAny(output!.Signal, joined).WaitAsync(token);
            if (first != output.Signal)
            { await joined; throw new IOException("The original child marker is absent."); }
            await output.Signal;
            Kill();
        }
        await joined.WaitAsync(token);
        Settled = process!.HasExited && exit!.IsCompleted && stdout!.IsCompleted && stderr!.IsCompleted;
        if (signal is null && process!.ExitCode != SuccessfulExit)
        { throw new IOException("The original movement child failed."); }
    }

    internal async Task JoinCleanupAsync(List<Exception> failures, CancellationToken token)
    {
        if (!started)
        {
            Settled = true;
            return;
        }
        ServerFailureObserver.Observe(Kill, failures);
        await ObserveAsync(exit, failures, token);
        await ObserveAsync(stdout, failures, token);
        await ObserveAsync(stderr, failures, token);
        var exited = false;
        ServerFailureObserver.Observe(() => exited = process!.HasExited, failures);
        Settled = exited && (exit is null || exit.IsCompleted)
            && (stdout is null || stdout.IsCompleted) && (stderr is null || stderr.IsCompleted);
    }

    /// <summary>Disposes only a wholly settled original process after actual exit and reader joins.</summary>
    public void Dispose()
    {
        if (!Settled)
        { throw new IOException("The original child or its readers have not joined."); }
        process?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Kill()
    {
        if (!process!.HasExited)
        { process.Kill(entireProcessTree: true); }
    }

    private static async Task ObserveAsync(Task? original, List<Exception> failures, CancellationToken token)
    {
        if (original is not null)
        { await ServerFailureObserver.ObserveAsync(() => original.WaitAsync(token), failures); }
    }
}

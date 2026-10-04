using System.Diagnostics;
using System.Text;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3OfflineProcessIo
{
    private static readonly UTF8Encoding OutputEncoding = new(false, true);

    internal static async Task<string> ReadAsync(Stream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[NodeEpochRf3OfflineProtocol.BufferBytes];
        int read;
        while ((read = await stream.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > NodeEpochRf3OfflineProtocol.MaximumOutputBytes)
            { throw new InvalidOperationException(NodeEpochRf3OfflineProtocol.OutputExceeded); }
            await output.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None).ConfigureAwait(false);
        }
        return OutputEncoding.GetString(output.GetBuffer().AsSpan(0, checked((int)output.Length)));
    }

    internal static async Task WaitAsync(Task exit, Task<string> output, Task<string> error,
        CancellationToken cancellationToken)
    {
        while (!exit.IsCompleted)
        {
            if (output.IsFaulted || output.IsCanceled)
            { _ = await output.ConfigureAwait(false); }
            if (error.IsFaulted || error.IsCanceled)
            { _ = await error.ConfigureAwait(false); }
            var pending = new List<Task> { exit };
            if (!output.IsCompleted)
            { pending.Add(output); }
            if (!error.IsCompleted)
            { pending.Add(error); }
            _ = await Task.WhenAny(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        await exit.WaitAsync(cancellationToken).ConfigureAwait(false);
        _ = await Task.WhenAll(output, error).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<List<Exception>> SettleAsync(Process process, Task exit,
        Task<string> output, Task<string> error)
    {
        var failures = new List<Exception>();
        try
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
        }
        catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            failures.Add(failure);
        }
        using var deadline = new CancellationTokenSource(NodeEpochRf3OfflineProtocol.CleanupDeadline);
        await ObserveAsync(exit, failures, deadline.Token).ConfigureAwait(false);
        await ObserveAsync(output, failures, deadline.Token).ConfigureAwait(false);
        await ObserveAsync(error, failures, deadline.Token).ConfigureAwait(false);
        return failures;
    }

    private static async Task ObserveAsync(Task task, List<Exception> failures, CancellationToken cancellationToken)
    {
        await ServerFailureObserver.ObserveAsync(() => task.WaitAsync(cancellationToken), failures).ConfigureAwait(false);
        if (!task.IsCompleted)
        {
            _ = task.ContinueWith(static terminal => _ = terminal.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}

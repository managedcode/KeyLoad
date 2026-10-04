using System.Diagnostics;
using System.Text;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Owns bounded child-process stream draining and cancellation cleanup.</summary>
internal static class ContainerRestartProcessIo
{
    private const int MaximumTerminationAttempts = 2;

    /// <summary>Terminates and observes the actual CLI process and both redirected readers.</summary>
    /// <param name="process">The actual Docker process.</param>
    /// <param name="processExit">The original process wait created during inspection, if any.</param>
    /// <param name="output">The bounded stdout reader.</param>
    /// <param name="error">The draining stderr reader.</param>
    internal static async Task KillAndObserveAsync(Process process, Task? processExit,
        Task<string>? output, Task<string>? error)
    {
        try
        {
            TerminateProcessTree(process);
        }
        finally
        {
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await ObserveStartedTasksAsync(processExit, output, error).ConfigureAwait(false);
            }
        }
    }

    private static void TerminateProcessTree(Process process)
    {
        for (var attempt = 0; attempt < MaximumTerminationAttempts && !process.HasExited; attempt++)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                return;
            }
            catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure)
                && (attempt + 1 < MaximumTerminationAttempts || process.HasExited))
            {
            }
        }
    }

    /// <summary>Waits for process exit while detecting a failed stream drain.</summary>
    /// <param name="processExit">The actual CLI process wait.</param>
    /// <param name="output">The bounded stdout reader.</param>
    /// <param name="error">The draining stderr reader.</param>
    /// <returns>False when a reader fails before the process exits.</returns>
    internal static async Task<bool> WaitForExitOrReaderFailureAsync(Task processExit,
        Task<string> output, Task<string> error)
    {
        while (!processExit.IsCompleted)
        {
            if (output.IsFaulted || output.IsCanceled || error.IsFaulted || error.IsCanceled)
            {
                return false;
            }

            var pending = new List<Task>(3) { processExit };
            if (!output.IsCompleted)
            {
                pending.Add(output);
            }

            if (!error.IsCompleted)
            {
                pending.Add(error);
            }

            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            if (completed != processExit && (completed.IsFaulted || completed.IsCanceled))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Observes the original process wait and every reader without retaining exception text.</summary>
    /// <param name="processExit">The optional original inspection wait.</param>
    /// <param name="output">The optional stdout reader created before a setup failure.</param>
    /// <param name="error">The optional stderr reader created before a setup failure.</param>
    internal static async Task ObserveStartedTasksAsync(Task? processExit, Task<string>? output, Task<string>? error)
    {
        var readers = new List<Task>(3);
        if (processExit is not null)
        {
            readers.Add(processExit);
        }

        if (output is not null)
        {
            readers.Add(output);
        }

        if (error is not null)
        {
            readers.Add(error);
        }

        try
        {
            await Task.WhenAll(readers).ConfigureAwait(false);
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
        }
    }

    /// <summary>Drains a stream while retaining at most the requested number of characters.</summary>
    /// <param name="reader">The actual redirected process stream.</param>
    /// <param name="maximumCharacters">The maximum retained output length; zero drains without retaining.</param>
    /// <param name="cancellationToken">The diagnostic-local timeout token.</param>
    /// <returns>The retained output prefix.</returns>
    internal static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 256));
        var buffer = new char[256];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            var remaining = maximumCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
        }

        return builder.ToString();
    }
}

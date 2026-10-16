using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2InventoryProcessStreams
{
    private const int MaximumCharacters = 8_192;
    private const string RetainedProcessKey = "D2InventoryOriginalProcess";
    private const string RetainedReadersKey = "D2InventoryOriginalReaders";

    internal static async Task<string> ReadAsync(StreamReader reader)
    {
        var buffer = new char[MaximumCharacters];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(count));
            if (read == 0)
            {
                return new string(buffer, 0, count);
            }
            count += read;
        }
        throw new InvalidOperationException("The owned D2 process exceeded its bounded pipe output.");
    }

    internal static async Task<bool> JoinAsync(Process? process, Task<string?>? marker,
        Task<string>? errors, Task<string>? output, List<Exception> failures, CancellationToken token)
    {
        var tasks = new Task?[] { marker, errors, output }.OfType<Task>().ToArray();
        var joined = Task.WhenAll(tasks);
        var waitingFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => joined.WaitAsync(token), waitingFailures);
        if (!joined.IsCompleted)
        {
            failures.AddRange(waitingFailures);
        }
        foreach (var task in tasks.Where(task => task.IsCompleted))
        {
            await ServerFailureObserver.ObserveAsync(() => task, failures);
        }
        if (tasks.Any(task => !task.IsCompleted))
        {
            var unknown = new IOException("The D2 original process readers did not settle; process and root were retained.");
            unknown.Data[RetainedProcessKey] = process;
            unknown.Data[RetainedReadersKey] = tasks;
            failures.Add(unknown);
            return false;
        }
        if (process is not null)
        {
            var exited = false;
            ServerFailureObserver.Observe(() => exited = process.HasExited, failures);
            return exited;
        }
        return true;
    }
}

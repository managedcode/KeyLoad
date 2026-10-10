using System.Runtime.ExceptionServices;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Joins a fixed admitted batch of independently authorized native children.</summary>
internal static class PartitionQueryParallelBatch
{
    private const int FirstTaskIndex = 0;
    private const int SingleFailure = 1;

    internal static Task<PartitionQueryOwnedLeaf[]> RunAsync(PartitionQueryPreparedLeaf[] leaves,
        int offset, int count, CancellationTokenSource execution)
        => RunAsync<PartitionQueryPreparedLeaf, PartitionQueryOwnedLeaf>(leaves, offset, count, execution,
            static (leaf, token) => leaf.RunAsync(token));

    internal static async Task<TResult[]> RunAsync<TLeaf, TResult>(TLeaf[] leaves,
        int offset, int count, CancellationTokenSource execution,
        Func<TLeaf, CancellationToken, Task<TResult>> run)
    {
        var tasks = new Task<TResult>[count];
        for (var index = FirstTaskIndex; index < count; index++)
        { tasks[index] = RunLeafAsync(leaves[offset + index], execution, run); }
        try
        { return await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (Exception primary)
        {
            var failures = tasks.Where(task => task.IsFaulted)
                .SelectMany(task => task.Exception!.InnerExceptions).ToArray();
            if (failures.Length > SingleFailure)
            { throw new AggregateException(failures); }
            if (failures.Length == SingleFailure)
            { ExceptionDispatchInfo.Capture(failures[FirstTaskIndex]).Throw(); }
            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    private static async Task<TResult> RunLeafAsync<TLeaf, TResult>(TLeaf leaf,
        CancellationTokenSource execution, Func<TLeaf, CancellationToken, Task<TResult>> run)
    {
        try
        {
            execution.Token.ThrowIfCancellationRequested();
            return await run(leaf, execution.Token).ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            try
            { await execution.CancelAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
}

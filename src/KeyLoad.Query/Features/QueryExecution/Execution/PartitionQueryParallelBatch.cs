using System.Runtime.ExceptionServices;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Joins a fixed admitted batch of independently authorized native children.</summary>
internal static class PartitionQueryParallelBatch
{
    private const int FirstTaskIndex = 0;
    private const int SingleFailure = 1;

    internal static async Task<PartitionQueryOwnedLeaf[]> RunAsync(PartitionQueryPreparedLeaf[] leaves,
        int offset, int count, CancellationTokenSource execution)
    {
        var tasks = new Task<PartitionQueryOwnedLeaf>[count];
        for (var index = FirstTaskIndex; index < count; index++)
        { tasks[index] = RunLeafAsync(leaves[offset + index], execution); }
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

    private static async Task<PartitionQueryOwnedLeaf> RunLeafAsync(PartitionQueryPreparedLeaf leaf,
        CancellationTokenSource execution)
    {
        try
        {
            execution.Token.ThrowIfCancellationRequested();
            return await leaf.RunAsync(execution.Token).ConfigureAwait(false);
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

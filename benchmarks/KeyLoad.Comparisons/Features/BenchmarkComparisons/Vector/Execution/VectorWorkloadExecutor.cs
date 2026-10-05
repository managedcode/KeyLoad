using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal sealed class VectorWorkloadExecutor(VectorComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions)
{
    internal async Task<VectorWorkloadObservations> RunAsync(IVectorComparisonTarget target,
        VectorComparisonCorpus corpus, IReadOnlyList<ReadOnlyMemory<float>> queries,
        IReadOnlyList<IReadOnlyList<VectorNeighbor>> expected, CancellationToken cancellationToken)
    {
        using var failure = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return await RunJoinedAsync(target, corpus, queries, expected, failure).ConfigureAwait(false);
    }

    private async Task<VectorWorkloadObservations> RunJoinedAsync(IVectorComparisonTarget target,
        VectorComparisonCorpus corpus, IReadOnlyList<ReadOnlyMemory<float>> queries,
        IReadOnlyList<IReadOnlyList<VectorNeighbor>> expected, CancellationTokenSource failure)
    {
        var token = failure.Token;
        var measured = new VectorWorkloadObservations(profile);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = new Task[profile.Concurrency];
        for (var index = VectorWorkloadExecutorValues.FirstIndex; index < workers.Length; index++)
        {
            workers[index] = CancelOnFailureAsync(start.Task, failure, measured,
                () => ExecuteQueriesAsync(target, corpus, queries, expected, measured, token));
        }
        var writer = profile.UpdateCount > VectorWorkloadExecutorValues.FirstIndex ? CancelOnFailureAsync(start.Task, failure, measured,
            () => new VectorUpdateExecutor(profile, executionOptions).RunAsync(target, corpus, measured, token)) : Task.CompletedTask;
        var timer = Stopwatch.StartNew();
        start.SetResult();
        try
        {
            await Task.WhenAll(CompleteQueriesAsync(workers, timer, measured), writer).ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (measured.Failure is { } original)
            {
                ExceptionDispatchInfo.Capture(original).Throw();
            }
            throw;
        }
        ValidateCompleted(measured);
        return measured;

    }

    private static async Task CompleteQueriesAsync(Task[] workers, Stopwatch timer, VectorWorkloadObservations measured)
    {
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        finally
        {
            timer.Stop();
            measured.QuerySeconds = timer.Elapsed.TotalSeconds;
            Volatile.Write(ref measured.QueryActive, VectorWorkloadExecutorValues.FirstIndex);
        }
    }

    private static async Task CancelOnFailureAsync(Task start, CancellationTokenSource failure, VectorWorkloadObservations measured, Func<Task> execute)
    {
        try
        {
            await start.ConfigureAwait(false);
            await execute().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Interlocked.CompareExchange(ref measured.Failure, error, null);
            await failure.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ExecuteQueriesAsync(IVectorComparisonTarget target, VectorComparisonCorpus corpus,
        IReadOnlyList<ReadOnlyMemory<float>> queries, IReadOnlyList<IReadOnlyList<VectorNeighbor>> expected,
        VectorWorkloadObservations measured, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = Interlocked.Increment(ref measured.NextQuery);
            if (operation >= profile.MeasuredQueries)
            {
                break;
            }
            var index = operation % queries.Count;
            var sample = SampleOrdinal(operation, profile.MeasuredQueries, profile.LatencySampleCount);
            var started = sample < VectorWorkloadExecutorValues.FirstIndex ? VectorWorkloadExecutorValues.FirstIndex : Stopwatch.GetTimestamp();
            using var deadline = VectorOperationDeadline.Create(executionOptions.Value, cancellationToken);
            var neighbors = await target.SearchAsync(queries[index], profile.TopK, profile.QueryMode,
                deadline.Token).ConfigureAwait(false);
            if (sample >= VectorWorkloadExecutorValues.FirstIndex)
            {
                measured.Latencies[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            measured.Recalls[operation] = VectorResponseValidator.CalculateRecall(corpus, neighbors, expected[index]);
            if (Volatile.Read(ref measured.UpdateActive) != VectorWorkloadExecutorValues.FirstIndex)
            {
                Interlocked.Increment(ref measured.QueriesDuringUpdates);
            }
            Interlocked.Increment(ref measured.QuerySuccesses);
        }
    }

    private void ValidateCompleted(VectorWorkloadObservations measured)
    {
        if (measured.QuerySuccesses != profile.MeasuredQueries || measured.UpdateSuccesses != profile.UpdateCount)
        {
            throw new InvalidDataException(VectorWorkloadExecutorValues.TheVectorWorkloadDidNotComplete);
        }
        if (profile.UpdateCount > VectorWorkloadExecutorValues.FirstIndex && (measured.QueriesDuringUpdates == VectorWorkloadExecutorValues.FirstIndex || measured.UpdatesDuringQueries == VectorWorkloadExecutorValues.FirstIndex))
        {
            throw new InvalidDataException(VectorWorkloadExecutorValues.MixedVectorSearchesAndUpdatesDid);
        }
        if (measured.Recalls.Average() < profile.MinimumRecall)
        {
            throw new InvalidDataException(VectorWorkloadExecutorValues.AggregateVectorRecallIsBelowThe);
        }
    }

    private static int SampleOrdinal(int operation, int operationCount, int sampleCount)
    {
        var ordinal = (int)((long)operation * sampleCount / operationCount);
        return ordinal < sampleCount && (int)((long)(operation + VectorWorkloadExecutorValues.SingleElementOffset) * sampleCount / operationCount) != ordinal
            ? ordinal : -VectorWorkloadExecutorValues.SingleElementOffset;
    }
}

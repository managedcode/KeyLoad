using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>Runs one lane; each request covers <paramref name="batch"/> consecutive ordinals and every ordinal keeps its own sample.</summary>
internal sealed class BenchmarkSessionLaneExecution(BenchmarkSessionContext context, BenchmarkSessionClientLease lease,
    Func<IComparisonSession?, int, int, CancellationToken, Task> operation, int count, int batch, bool documentSessions)
{
    private readonly BenchmarkSessionOperationSample[] samples = new BenchmarkSessionOperationSample[count];
    private int attempts;
    private int successes;
    private int failed;
    private int canceled;
    private int settled;
    private long origin;

    internal async Task<BenchmarkSessionLaneReport> RunAsync(string id, int clients, BenchmarkSessionStartBarrier? barrier, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var resources = new ClientResourceSampler(context.ExecutionOptions, context.Clock);
        await RunCohortAsync(clients, barrier, stop, token).ConfigureAwait(false);
        var last = context.Clock.GetTimestamp();
        ObserveProgress();
        var observed = await resources.StopAsync().ConfigureAwait(false);
        var seconds = Math.Max(double.Epsilon, context.Clock.GetElapsedTime(origin, last).TotalSeconds);
        var retained = samples.Take(attempts).ToImmutableArray();
        var latency = retained.IsEmpty ? new Latencies(BenchmarkSessionLimits.NoObservedItems, BenchmarkSessionLimits.NoObservedItems, BenchmarkSessionLimits.NoObservedItems)
            : ComparisonStatistics.Percentiles(retained.Select(sample => sample.CompletedMs - sample.StartedMs));
        var measurement = new Measurement(attempts, successes, attempts - successes, seconds, successes / seconds,
            latency, BenchmarkSessionLimits.NoObservedItems, null, null, null, observed);
        return new(id, measurement, retained.IsEmpty ? BenchmarkSessionLimits.NoObservedItems : retained.Min(sample => sample.StartedMs),
            context.Clock.GetElapsedTime(origin, last).TotalMilliseconds, retained);
    }

    private async Task RunCohortAsync(int clients, BenchmarkSessionStartBarrier? barrier,
        CancellationTokenSource stop, CancellationToken token)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(BenchmarkSessionLimits.NoObservedItems, clients)
            .Select(worker => ExecuteWorkerAsync(worker, clients, start.Task, stop)).ToArray();
        var joined = Task.WhenAll(workers);
        try
        {
            if (barrier is not null)
            {
                await barrier.ReadyAsync().WaitAsync(token).ConfigureAwait(false);
            }
            origin = barrier?.Origin ?? context.Clock.GetTimestamp();
            start.SetResult();
            await joined.ConfigureAwait(false);
        }
        finally
        {
            // Cancel unopened workers and join the original bounded cohort before releasing execution clients.
            start.TrySetCanceled(token);
            _ = (await RuntimeComparisonFailure.ObserveAsync(joined).ConfigureAwait(false)).Value;
        }
    }

    private async Task ExecuteWorkerAsync(int worker, int clients, Task start, CancellationTokenSource stop)
    {
        await start.ConfigureAwait(false);
        // Each client owns its first request and a disjoint stride, even when every execution await completes synchronously.
        for (var first = worker * batch; first < count && !stop.IsCancellationRequested; first += clients * batch)
        {
            await ExecuteAttemptAsync(worker, first, Math.Min(batch, count - first), stop).ConfigureAwait(false);
        }
    }

    private async Task ExecuteAttemptAsync(int worker, int first, int length, CancellationTokenSource stop)
    {
        using var deadline = ComparisonDeadline.Create(context.Options.TimeoutSeconds, context.Clock, stop.Token);
        var started = context.Clock.GetTimestamp();
        var position = Interlocked.Add(ref attempts, length) - length;
        var succeeded = false;
        string? failure = null;
        try
        {
            await operation((lease.At(worker, documentSessions)).Value, first, length, deadline.Token).ConfigureAwait(false);
            Interlocked.Add(ref successes, length);
            succeeded = true;
        }
        catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
        {
            failure = ComparisonErrors.Safe(error);
            if (error is OperationCanceledException)
            {
                Interlocked.Add(ref canceled, length);
            }
            else
            {
                Interlocked.Add(ref failed, length);
            }
            await stop.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            // A batched record is acknowledged when its request is, so it carries that request's start and completion.
            var startedMs = context.Clock.GetElapsedTime(origin, started).TotalMilliseconds;
            var completedMs = context.Clock.GetElapsedTime(origin).TotalMilliseconds;
            for (var offset = BenchmarkSessionLimits.NoObservedItems; offset < length; offset++)
            {
                samples[position + offset] = new(first + offset, worker, startedMs, completedMs, succeeded, failure);
            }
            var completed = Interlocked.Add(ref settled, length);
            if (completed / BenchmarkSessionLimits.ProgressCadence != (completed - length) / BenchmarkSessionLimits.ProgressCadence
                || completed == count)
            {
                ObserveProgress();
            }
        }
    }

    private void ObserveProgress() => context.Progress?.Invoke(new(BenchmarkSessionLimits.NoObservedItems,
        Volatile.Read(ref attempts), Volatile.Read(ref successes), count, Volatile.Read(ref failed), Volatile.Read(ref canceled),
        context.Clock.GetElapsedTime(origin).TotalSeconds));
}

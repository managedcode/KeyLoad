namespace KeyLoad.ComparisonHost.Measurement;

/// <summary>Runs a fixed amount of work on concurrent clients and times every call.</summary>
internal static class OperationLanes
{
    /// <summary>
    /// Runs one call per operation index on <paramref name="clients"/> concurrent clients. When <paramref name="limit"/>
    /// has passed, clients take no new operation; calls already started finish and the result counts only what ran.
    /// </summary>
    internal static Task<CaseResult> RunAsync(string id, int operations, int clients, TimeSpan limit,
        Func<int, CancellationToken, Task> operation, CancellationToken token)
        => RunBatchesAsync(id, operations, 1, clients, (first, _, cancellation) => operation(first, cancellation), token, limit);

    /// <summary>Splits <paramref name="units"/> into calls of up to <paramref name="batchSize"/> units each.</summary>
    internal static async Task<CaseResult> RunBatchesAsync(string id, int units, int batchSize, int clients,
        Func<int, int, CancellationToken, Task> call, CancellationToken token, TimeSpan? limit = null)
    {
        var clock = TimeProvider.System;
        var budget = limit ?? Timeout.InfiniteTimeSpan;
        var calls = (units + batchSize - 1) / batchSize;
        var latencies = new double[calls];
        var failures = new FailureCounter();
        var next = -1;
        var started = clock.GetTimestamp();

        async Task ClientAsync()
        {
            int index;
            while ((budget == Timeout.InfiniteTimeSpan || clock.GetElapsedTime(started) < budget)
                && (index = Interlocked.Increment(ref next)) < calls)
            {
                var first = index * batchSize;
                var count = Math.Min(batchSize, units - first);
                var begin = clock.GetTimestamp();
                try
                {
                    await call(first, count, token);
                    latencies[index] = clock.GetElapsedTime(begin).TotalMilliseconds;
                }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    latencies[index] = double.NaN;
                    failures.Add(error, count);
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Max(1, Math.Min(clients, calls))).Select(_ => ClientAsync()));
        var ran = Math.Min(Volatile.Read(ref next) + 1, calls);
        var attempted = Math.Min(units, ran * batchSize);
        return CaseResult.Measured(id, units, attempted, batchSize, latencies.AsSpan(0, ran).ToArray(), failures,
            clock.GetElapsedTime(started));
    }
}

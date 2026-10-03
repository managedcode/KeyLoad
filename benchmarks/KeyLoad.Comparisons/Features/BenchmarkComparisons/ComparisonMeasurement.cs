using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyLoad.Comparisons;

internal sealed class ComparisonMeasurer(ComparisonOptions options)
{
    private const string FailedAttemptDetail = "Failed attempts remain in raw samples and latency; useful throughput counts verified successes.";

    public async Task<ComparisonCase> MeasureAsync(IComparisonTarget target, BenchmarkDataset dataset, Scenario scenario,
        int repetition, CancellationToken cancellationToken)
    {
        var sessions = new List<IComparisonSession>();
        ComparisonCase result;
        bool closed;
        try
        {
            result = await MeasureOwnedAsync(target, dataset, scenario, repetition, sessions, cancellationToken);
        }
        finally
        {
            closed = await ComparisonSessionCleanup.CloseAsync(sessions, options.TimeoutSeconds);
        }
        return closed ? result : result with { Status = ComparisonStatuses.Failed, Detail = ComparisonSessionCleanup.Failure };
    }

    private async Task<ComparisonCase> MeasureOwnedAsync(IComparisonTarget target, BenchmarkDataset dataset,
        Scenario scenario, int repetition, List<IComparisonSession> sessions, CancellationToken cancellationToken)
    {
        var inputs = Enumerable.Range(0, options.Operations).Select(operation => dataset.Input(scenario, repetition, operation, false)).ToArray();
        for (var worker = 0; worker < options.Concurrency; worker++)
        {
            sessions.Add(await target.OpenSessionAsync(cancellationToken));
        }

        await WarmupAsync(sessions, dataset, scenario, repetition, cancellationToken);
        await ComparisonMutationPreparation.PrepareAsync(sessions, inputs, scenario, options.TimeoutSeconds, cancellationToken);
        var samples = new OperationSample[options.Operations];
        var outputs = new OperationResult?[options.Operations];
        await using var resources = new ClientResourceSampler();
        var clock = Stopwatch.StartNew();
        await ExecuteBatchAsync(sessions, inputs, scenario, samples, outputs, clock, cancellationToken);
        clock.Stop();
        var clientResources = await resources.StopAsync();
        if (ComparisonMutationPreparation.Required(scenario))
        {
            await ComparisonMutationValidation.ValidateAsync(sessions, dataset, scenario, inputs, samples, outputs, cancellationToken);
        }
        else
        {
            await ComparisonValidation.ValidateBatchAsync(sessions[0], dataset, scenario, inputs, samples, outputs, cancellationToken);
        }
        var measurement = ComparisonStatistics.Summarize(samples, clock.Elapsed.TotalSeconds) with { ClientResources = clientResources };
        return new(target.Profile.Name, scenario, repetition, measurement.Failures == 0 ? ComparisonStatuses.Measured : ComparisonStatuses.Failed,
            measurement.Failures == 0 ? null : FailedAttemptDetail, measurement,
            ImmutableCollectionsMarshal.AsImmutableArray(samples));
    }

    private async Task WarmupAsync(List<IComparisonSession> sessions, BenchmarkDataset dataset, Scenario scenario,
        int repetition, CancellationToken cancellationToken)
    {
        var warmup = Enumerable.Range(0, options.Warmup).Select(operation => dataset.Input(scenario, repetition, operation, true)).ToArray();
        await ComparisonMutationPreparation.PrepareAsync(sessions, warmup, scenario, options.TimeoutSeconds, cancellationToken);
        for (var operation = 0; operation < options.Warmup; operation++)
        {
            using var deadline = ComparisonDeadline.Create(options.TimeoutSeconds, cancellationToken);
            var input = warmup[operation];
            var session = sessions[operation % sessions.Count];
            var output = await session.ExecuteAsync(scenario, input, deadline.Token);
            await ComparisonValidation.ValidateOperationAsync(session, dataset, scenario, input, output, deadline.Token);
        }
    }

    private async Task ExecuteBatchAsync(List<IComparisonSession> sessions, BenchmarkDocument[] inputs, Scenario scenario,
        OperationSample[] samples, OperationResult?[] outputs, Stopwatch clock, CancellationToken cancellationToken)
    {
        var next = -1;
        await Task.WhenAll(sessions.Select(async (session, worker) =>
        {
            while (true)
            {
                var operation = Interlocked.Increment(ref next);
                if (operation >= inputs.Length)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                using var deadline = ComparisonDeadline.Create(options.TimeoutSeconds, cancellationToken);
                var started = clock.Elapsed.TotalMilliseconds;
                string? failure = null;
                try
                { outputs[operation] = await session.ExecuteAsync(scenario, inputs[operation], deadline.Token); }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested) { failure = ComparisonErrors.Safe(error); }
                samples[operation] = new(operation, worker, started, clock.Elapsed.TotalMilliseconds, failure is null, failure,
                    Encoding.UTF8.GetByteCount(inputs[operation].Json), outputs[operation]?.Message?.Id, outputs[operation]?.Queue);
            }
        }));
    }

}

internal static class ComparisonStatistics
{
    public static Measurement Summarize(OperationSample[] samples, double elapsedSeconds)
    {
        if (samples.Length == 0 || elapsedSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(samples));
        }

        var successes = samples.Count(sample => sample.Success);
        var queue = samples.Where(sample => sample.Queue is not null).Select(sample => sample.Queue!).ToArray();
        return new(samples.Length, successes, samples.Length - successes, elapsedSeconds, successes / elapsedSeconds,
            Percentiles(samples.Select(sample => sample.LatencyMs)),
            samples.Where(sample => sample.Success && sample.CompletedMessageId is not null).Select(sample => sample.CompletedMessageId).Distinct().Count(),
            queue.Length == 0 ? null : Percentiles(queue.Select(item => item.EnqueueMs)),
            queue.Length == 0 ? null : Percentiles(queue.Select(item => item.ReceiveMs)),
            queue.Length == 0 ? null : Percentiles(queue.Select(item => item.AckMs)));
    }

    public static Latencies Percentiles(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }

        double At(double percentile) => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new(At(.50), At(.95), At(.99));
    }
}

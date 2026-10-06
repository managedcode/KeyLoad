using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal sealed class ComparisonMeasurer(IOptions<ComparisonOptions> workloadOptions, ComparisonProgressObserver observer,
    IOptions<NativeComparisonExecutionOptions> executionOptions)
{
    private readonly ComparisonOptions options = workloadOptions.Value;
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
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;
        const int NoObservedItems = 0;

        observer.Begin(ComparisonProgressPhase.Initialize, repetition + SingleItemCount);
        var inputs = Enumerable.Range(FirstElementIndex, options.Operations).Select(operation => dataset.Input(scenario, repetition, operation, false)).ToArray();
        for (var worker = NoObservedItems; worker < options.Concurrency; worker++)
        {
            sessions.Add(await target.OpenSessionAsync(cancellationToken));
        }

        await WarmupAsync(sessions, dataset, scenario, repetition, cancellationToken);
        observer.Begin(ComparisonProgressPhase.Prepare, repetition + SingleItemCount);
        await ComparisonMutationPreparation.PrepareAsync(sessions, inputs, scenario, options.TimeoutSeconds, cancellationToken);
        var samples = new OperationSample[options.Operations];
        var outputs = new OperationResult?[options.Operations];
        await using var resources = new ClientResourceSampler(executionOptions);
        observer.Begin(ComparisonProgressPhase.Measure, repetition + SingleItemCount, options.Operations);
        var clock = Stopwatch.StartNew();
        await ExecuteBatchAsync(sessions, inputs, scenario, samples, outputs, clock, cancellationToken);
        clock.Stop();
        var clientResources = await resources.StopAsync();
        observer.Begin(ComparisonProgressPhase.Validate, repetition + SingleItemCount, samples.Length, samples.Length,
            samples.Count(sample => !sample.Success));
        if (ComparisonMutationPreparation.Required(scenario))
        {
            await ComparisonMutationValidation.ValidateAsync(sessions, dataset, scenario, inputs, samples, outputs, cancellationToken);
        }
        else
        {
            await ComparisonValidation.ValidateBatchAsync(sessions[FirstElementIndex], dataset, scenario, inputs, samples, outputs, cancellationToken);
        }
        var measurement = ComparisonStatistics.Summarize(samples, clock.Elapsed.TotalSeconds) with { ClientResources = clientResources };
        observer.Begin(ComparisonProgressPhase.Complete, repetition + SingleItemCount, measurement.Attempts, measurement.Attempts, measurement.Failures);
        return new(target.Profile.Name, scenario, repetition, measurement.Failures == NoObservedItems ? ComparisonStatuses.Measured : ComparisonStatuses.Failed,
            measurement.Failures == NoObservedItems ? null : FailedAttemptDetail, measurement,
            ImmutableCollectionsMarshal.AsImmutableArray(samples));
    }

    private async Task WarmupAsync(List<IComparisonSession> sessions, BenchmarkDataset dataset, Scenario scenario,
        int repetition, CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;
        const int SingleItemCount = 1;

        var warmup = Enumerable.Range(FirstElementIndex, options.Warmup).Select(operation => dataset.Input(scenario, repetition, operation, true)).ToArray();
        observer.Begin(ComparisonProgressPhase.Prepare, repetition + SingleItemCount);
        await ComparisonMutationPreparation.PrepareAsync(sessions, warmup, scenario, options.TimeoutSeconds, cancellationToken);
        observer.Begin(ComparisonProgressPhase.Warmup, repetition + SingleItemCount, options.Warmup);
        for (var operation = FirstElementIndex; operation < options.Warmup; operation++)
        {
            using var deadline = ComparisonDeadline.Create(options.TimeoutSeconds, cancellationToken);
            var input = warmup[operation];
            var session = sessions[operation % sessions.Count];
            var success = false;
            try
            {
                var output = await session.ExecuteAsync(scenario, input, deadline.Token);
                await ComparisonValidation.ValidateOperationAsync(session, dataset, scenario, input, output, deadline.Token);
                success = true;
            }
            finally
            {
                observer.Settle(success);
            }
        }
    }

    private async Task ExecuteBatchAsync(List<IComparisonSession> sessions, BenchmarkDocument[] inputs, Scenario scenario,
        OperationSample[] samples, OperationResult?[] outputs, Stopwatch clock, CancellationToken cancellationToken)
    {
        const int MissingItemIndex = -1;

        var next = MissingItemIndex;
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
                await ExecuteAttemptAsync(session, inputs, scenario, samples, outputs, clock, operation, worker, cancellationToken);
            }
        }));
    }

    private async Task ExecuteAttemptAsync(IComparisonSession session, BenchmarkDocument[] inputs, Scenario scenario,
        OperationSample[] samples, OperationResult?[] outputs, Stopwatch clock, int operation, int worker, CancellationToken cancellationToken)
    {
        using var deadline = ComparisonDeadline.Create(options.TimeoutSeconds, cancellationToken);
        var started = clock.Elapsed.TotalMilliseconds;
        var success = false;
        try
        {
            string? failure = null;
            try
            { outputs[operation] = await session.ExecuteAsync(scenario, inputs[operation], deadline.Token); }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested) { failure = ComparisonErrors.Safe(error); }
            samples[operation] = new(operation, worker, started, clock.Elapsed.TotalMilliseconds, failure is null, failure,
                Encoding.UTF8.GetByteCount(inputs[operation].Json), outputs[operation]?.Message?.Id, outputs[operation]?.Queue);
            success = failure is null;
        }
        finally
        {
            observer.Settle(success);
        }
    }
}

internal static class ComparisonStatistics
{
    private const int SingleItemCount = 1;

    public static Measurement Summarize(OperationSample[] samples, double elapsedSeconds)
    {
        const int NoItems = 0;

        if (samples.Length == NoItems || elapsedSeconds <= NoItems)
        {
            throw new ArgumentOutOfRangeException(nameof(samples));
        }

        var successes = samples.Count(sample => sample.Success);
        var queue = samples.Where(sample => sample.Queue is not null).Select(sample => sample.Queue!).ToArray();
        return new(samples.Length, successes, samples.Length - successes, elapsedSeconds, successes / elapsedSeconds,
            Percentiles(samples.Select(sample => sample.LatencyMs)),
            samples.Where(sample => sample.Success && sample.CompletedMessageId is not null).Select(sample => sample.CompletedMessageId).Distinct().Count(),
            queue.Length == NoItems ? null : Percentiles(queue.Select(item => item.EnqueueMs)),
            queue.Length == NoItems ? null : Percentiles(queue.Select(item => item.ReceiveMs)),
            queue.Length == NoItems ? null : Percentiles(queue.Select(item => item.AckMs)));
    }

    public static Latencies Percentiles(IEnumerable<double> values)
    {
        const int NoItems = 0;
        const double MedianQuantileDouble = .50;
        const double TailP95QuantileDouble = .95;
        const double PercentileIdentity = .99;

        var sorted = values.Order().ToArray();
        if (sorted.Length == NoItems)
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }

        double At(double percentile) => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - SingleItemCount, NoItems, sorted.Length - SingleItemCount)];
        return new(At(MedianQuantileDouble), At(TailP95QuantileDouble), At(PercentileIdentity));
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyLoad.Comparisons;

public sealed class ComparisonRunner(ComparisonOptions options, Action<string>? progress = null)
{
    public async Task<ComparisonReport> RunAsync(IComparisonTarget[] targets, string? sourceRevision, CancellationToken cancellationToken,
        string storage = "unrecorded")
    {
        if (targets.Length == 0) throw new ArgumentException("At least one target is required.", nameof(targets));
        var started = DateTimeOffset.UtcNow;
        var dataset = new BenchmarkDataset(options);
        var cases = new List<ComparisonCase>();
        // Precompute once, before timing and before concurrent workers access the immutable oracle.
        for (var i = 0; i < Math.Max(options.Operations, options.Warmup); i++)
        {
            dataset.ExactNeighbors(dataset.Input(Scenario.VectorExact, 0, i, false));
            var root = dataset.Input(Scenario.GraphTraverse, 0, i, false);
            dataset.Reachable(root, 1); dataset.Reachable(root, options.GraphDepth);
        }
        for (var repetition = 0; repetition < options.Repetitions; repetition++)
        {
            // Rotate order to avoid always giving one engine the coldest machine/cache.
            foreach (var target in targets.Skip(repetition % targets.Length).Concat(targets.Take(repetition % targets.Length)))
            {
                string? initializationFailure = null;
                if (repetition == 0)
                {
                    progress?.Invoke($"Preparing {target.Profile.Name}");
                    try { await target.InitializeAsync(dataset, cancellationToken); }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                    { initializationFailure = SafeError(error); }
                }
                else if (cases.Any(item => item.Target == target.Profile.Name && item.Detail?.StartsWith("setup:", StringComparison.Ordinal) == true))
                    initializationFailure = "PreviousSetupFailure";
                foreach (var scenario in Enum.GetValues<Scenario>())
                {
                    if (!target.Supports(scenario))
                    {
                        cases.Add(new(target.Profile.Name, scenario, repetition, "unsupported", "Outside this target's shared contract.", null, []));
                        continue;
                    }
                    if (initializationFailure is not null)
                    {
                        cases.Add(new(target.Profile.Name, scenario, repetition, "failed", "setup:" + initializationFailure, null, []));
                        continue;
                    }
                    progress?.Invoke($"{target.Profile.Name}: {scenario}, repetition {repetition + 1}/{options.Repetitions}");
                    try { cases.Add(await MeasureAsync(target, dataset, scenario, repetition, cancellationToken)); }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                    { cases.Add(new(target.Profile.Name, scenario, repetition, "failed", SafeError(error), null, [])); }
                }
            }
        }
        return new(2, Guid.NewGuid(), started, options, dataset.Sha256, "closed-loop; setup, oracle, validation and warmup excluded",
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            RuntimeInformation.FrameworkDescription, storage, sourceRevision, targets.Select(target => target.Profile).ToArray(), cases.ToArray());
    }

    private async Task<ComparisonCase> MeasureAsync(IComparisonTarget target, BenchmarkDataset dataset, Scenario scenario,
        int repetition, CancellationToken cancellationToken)
    {
        var sessions = new List<IComparisonSession>();
        try
        {
            var inputs = Enumerable.Range(0, options.Operations).Select(i => dataset.Input(scenario, repetition, i, false)).ToArray();
            var warmupInputs = Enumerable.Range(0, options.Warmup).Select(i => dataset.Input(scenario, repetition, i, true)).ToArray();
            for (var i = 0; i < options.Concurrency; i++) sessions.Add(await target.OpenSessionAsync(cancellationToken));
            // Warmup failures fail the case. Queue warmup must drain before measured messages exist.
            for (var i = 0; i < options.Warmup; i++)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                var input = warmupInputs[i];
                var output = await sessions[i % sessions.Count].ExecuteAsync(scenario, input, deadline.Token);
                Validate(scenario, input, output, dataset);
            }
            var samples = new OperationSample[options.Operations];
            var outputs = new OperationResult?[options.Operations];
            var next = -1;
            await using var resources = new ClientResourceSampler();
            var clock = Stopwatch.StartNew();
            await Task.WhenAll(sessions.Select(async (session, worker) =>
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= options.Operations) return;
                    cancellationToken.ThrowIfCancellationRequested();
                    var input = inputs[index];
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                    var begin = clock.Elapsed.TotalMilliseconds;
                    string? failure = null;
                    try { outputs[index] = await session.ExecuteAsync(scenario, input, deadline.Token); }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested) { failure = SafeError(error); }
                    samples[index] = new(index, worker, begin, clock.Elapsed.TotalMilliseconds, failure is null, failure,
                        Encoding.UTF8.GetByteCount(input.Json), outputs[index]?.Message?.Id, outputs[index]?.Queue);
                }
            }));
            clock.Stop();
            var clientResources = await resources.StopAsync();
            // Correctness is outside the timer; an incorrect/duplicate result becomes a failed measured attempt.
            var expectedMessages = scenario == Scenario.QueueCycle ? inputs.ToDictionary(document => document.Id, StringComparer.Ordinal) : [];
            var completedMessages = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < samples.Length; i++)
            {
                if (!samples[i].Success) continue;
                try
                {
                    var input = inputs[i];
                    var output = outputs[i]!;
                    if (scenario == Scenario.QueueCycle)
                    {
                        if (output.Message is null || !expectedMessages.TryGetValue(output.Message.Id, out var expected)
                            || !BenchmarkDataset.SameDocument(output.Message, expected) || !completedMessages.Add(output.Message.Id))
                            throw new ComparisonFailure("QueueMissingDuplicateOrWrongPayload");
                    }
                    else if (scenario == Scenario.DocumentWrite)
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                        if (!BenchmarkDataset.SameDocument(await sessions[0].ReadAsync(input, deadline.Token), input))
                            throw new ComparisonFailure("WriteReadbackMismatch");
                    }
                    else Validate(scenario, input, output, dataset);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                { samples[i] = samples[i] with { Success = false, Error = SafeError(error) }; }
            }
            var measurement = Summarize(samples, clock.Elapsed.TotalSeconds) with { ClientResources = clientResources };
            return new(target.Profile.Name, scenario, repetition, measurement.Failures == 0 ? "measured" : "failed",
                measurement.Failures == 0 ? null : "Failed attempts remain in raw samples and latency; useful throughput counts verified successes.", measurement, samples);
        }
        finally { foreach (var session in sessions) await session.DisposeAsync(); }
    }

    private static void Validate(Scenario scenario, BenchmarkDocument input, OperationResult output, BenchmarkDataset dataset)
    {
        if (scenario == Scenario.PointRead && !BenchmarkDataset.SameDocument(output.Document, input))
            throw new ComparisonFailure("PointReadMismatch");
        if (scenario == Scenario.QueueCycle && !BenchmarkDataset.SameDocument(output.Message, input))
            throw new ComparisonFailure("WarmupQueueMismatch");
        if (scenario == Scenario.VectorExact)
        {
            var expected = dataset.ExactNeighbors(input);
            if (output.Neighbors is null || !output.Neighbors.Select(item => item.Id).SequenceEqual(expected.Select(item => item.Id))
                || output.Neighbors.Where((document, i) => !BenchmarkDataset.SameJson(document.Json, expected[i].Json)).Any())
                throw new ComparisonFailure("ExactRecallOrProjectionMismatch");
        }
        if (scenario is Scenario.GraphNeighbors or Scenario.GraphTraverse)
        {
            var expected = dataset.Reachable(input, scenario == Scenario.GraphNeighbors ? 1 : dataset.Options.GraphDepth);
            if (output.Vertices is null || !output.Vertices.SequenceEqual(expected, StringComparer.Ordinal))
                throw new ComparisonFailure("GraphReachabilityMismatch");
        }
    }

    public static Measurement Summarize(OperationSample[] samples, double elapsedSeconds)
    {
        if (samples.Length == 0 || elapsedSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(samples));
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
        if (sorted.Length == 0) throw new ArgumentOutOfRangeException(nameof(values));
        double At(double percentile) => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new(At(.50), At(.95), At(.99));
    }

    // Exception messages from drivers can contain connection strings or credentials.
    private static string SafeError(Exception error) => error is ComparisonFailure ? error.Message
        : $"{error.GetType().Name} at {error.TargetSite?.DeclaringType?.Name}.{error.TargetSite?.Name}";
}

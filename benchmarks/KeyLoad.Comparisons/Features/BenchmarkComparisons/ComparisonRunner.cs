using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace KeyLoad.Comparisons;

/// <summary>Runs the shared workload and records every measured attempt against supplied database targets.</summary>
/// <param name="options">The validated workload, corpus and measurement settings.</param>
/// <param name="progress">An optional observer of setup and case progress.</param>
public sealed class ComparisonRunner(ComparisonOptions options, Action<string>? progress = null)
{
    private const string SetupPrefix = "setup:";
    private const string PreviousSetupFailure = "PreviousSetupFailure";
    private const string LoadModel = "closed-loop; setup, oracle, validation and warmup excluded";
    private const string UnrecordedStorage = "unrecorded";
    private const string IsolatedTargetRequired = "An isolated scenario requires exactly one database target.";

    /// <summary>Prepares the oracle, initializes targets and runs their supported scenarios in rotated repetition order.</summary>
    /// <param name="targets">The targets to measure; the collection must contain at least one target.</param>
    /// <param name="sourceRevision">The measured source revision recorded as provenance, when available.</param>
    /// <param name="cancellationToken">The token cancelling setup and workload operations.</param>
    /// <param name="storage">The supplied storage-profile description recorded in the report.</param>
    /// <param name="scenario">One workload for an isolated worker, or null for the existing complete suite.</param>
    /// <returns>The report with target profiles, all case outcomes and all measured attempts.</returns>
    public async Task<ComparisonReport> RunAsync(IComparisonTarget[] targets, string? sourceRevision, CancellationToken cancellationToken,
        string storage = UnrecordedStorage, Scenario? scenario = null)
    {
        ValidateTargets(targets, scenario);

        var started = TimeProvider.System.GetUtcNow();
        var dataset = new BenchmarkDataset(options);
        var cases = new List<ComparisonCase>();
        PrepareOracle(dataset, scenario);
        for (var repetition = 0; repetition < options.Repetitions; repetition++)
        {
            var offset = repetition % targets.Length;
            foreach (var target in targets.Skip(offset).Concat(targets.Take(offset)))
            {
                await RunTargetAsync(target, dataset, repetition, cases, scenario, cancellationToken);
            }
        }
        return new(3, Guid.NewGuid(), started, options, dataset.Sha256, LoadModel,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            RuntimeInformation.FrameworkDescription, storage, sourceRevision,
            targets.Select(target => target.Profile).ToImmutableArray(), cases.ToImmutableArray());
    }

    private static void ValidateTargets(IComparisonTarget[] targets, Scenario? scenario)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (scenario is { } selected && !Enum.IsDefined(selected))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        if (scenario is not null && targets.Length != 1)
        {
            throw new ArgumentException(IsolatedTargetRequired, nameof(targets));
        }
        if (targets.Length == 0)
        {
            throw new ArgumentException("At least one target is required.", nameof(targets));
        }
    }

    private void PrepareOracle(BenchmarkDataset dataset, Scenario? selectedScenario)
    {
        for (var operation = 0; operation < Math.Max(options.Operations, options.Warmup); operation++)
        {
            if (selectedScenario is null or Scenario.VectorExact)
            {
                dataset.ExactNeighbors(dataset.Input(Scenario.VectorExact, 0, operation, false));
            }
            if (selectedScenario is null or Scenario.GraphNeighbors or Scenario.GraphTraverse)
            {
                var root = dataset.Input(Scenario.GraphTraverse, 0, operation, false);
                dataset.Reachable(root, 1);
                dataset.Reachable(root, options.GraphDepth);
            }
        }
    }

    private async Task RunTargetAsync(IComparisonTarget target, BenchmarkDataset dataset, int repetition,
        List<ComparisonCase> cases, Scenario? selectedScenario, CancellationToken cancellationToken)
    {
        var scenarios = selectedScenario is { } selected ? [selected] : Enum.GetValues<Scenario>();
        string? failure = null;
        if (repetition == 0)
        {
            progress?.Invoke($"Preparing {target.Profile.Name}");
            try
            {
                await target.InitializeAsync(dataset, cancellationToken);
                if (selectedScenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete
                    && target.Supports(Scenario.DocumentUpdate) && target.Supports(Scenario.DocumentDelete))
                {
                    await ComparisonMutationProbe.VerifyAsync(target, dataset, cancellationToken);
                }
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested) { failure = ComparisonErrors.Safe(error); }
        }
        else if (cases.Any(item => item.Target == target.Profile.Name && item.Detail?.StartsWith(SetupPrefix, StringComparison.Ordinal) == true))
        {
            failure = PreviousSetupFailure;
        }

        foreach (var scenario in scenarios)
        {
            cases.Add(await RunCaseAsync(target, dataset, scenario, repetition, failure, cancellationToken));
        }
    }

    private async Task<ComparisonCase> RunCaseAsync(IComparisonTarget target, BenchmarkDataset dataset,
        Scenario scenario, int repetition, string? setupFailure, CancellationToken cancellationToken)
    {
        if (setupFailure is not null)
        {
            var failed = new ComparisonCase(target.Profile.Name, scenario, repetition,
                ComparisonStatuses.Failed, SetupPrefix + setupFailure, null, []);
            await ComparisonFailureDiagnostics.ObserveAsync(target, failed, cancellationToken);
            return failed;
        }

        if (!target.Supports(scenario))
        {
            return new(target.Profile.Name, scenario, repetition, ComparisonStatuses.Unsupported, target.UnsupportedReason, null, []);
        }

        progress?.Invoke($"{target.Profile.Name}: {scenario}, repetition {repetition + 1}/{options.Repetitions}");
        ComparisonCase result;
        try
        { result = await new ComparisonMeasurer(options).MeasureAsync(target, dataset, scenario, repetition, cancellationToken); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        { result = new(target.Profile.Name, scenario, repetition, ComparisonStatuses.Failed, ComparisonErrors.Safe(error), null, []); }
        await ComparisonFailureDiagnostics.ObserveAsync(target, result, cancellationToken);
        return result;
    }

    /// <summary>Summarizes all attempts, retaining failed latencies while counting only successful useful operations.</summary>
    /// <param name="samples">The nonempty attempt collection, including failures.</param>
    /// <param name="elapsedSeconds">The positive measured duration of the complete case.</param>
    /// <returns>The throughput, attempt counts, latencies and available queue phase summaries.</returns>
    public static Measurement Summarize(OperationSample[] samples, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return ComparisonStatistics.Summarize(samples, elapsedSeconds);
    }

    /// <summary>Calculates nearest-rank latency percentiles from a nonempty set of durations.</summary>
    /// <param name="values">The measured durations in milliseconds.</param>
    /// <returns>The median, 95th and 99th percentile durations.</returns>
    public static Latencies Percentiles(IEnumerable<double> values) => ComparisonStatistics.Percentiles(values);
}

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Runs the shared materialized control or one accepted bounded scaled profile.</summary>
public sealed class ComparisonRunner
{
    private readonly TimeProvider timeProvider;
    private readonly ComparisonOptions? options;
    private readonly IOptions<ComparisonOptions>? workloadOptions;
    private readonly ScaledComparisonProfile? scaledProfile;
    private readonly VectorComparisonProfile? vectorProfile;
    private readonly IOptions<NativeComparisonExecutionOptions>? vectorExecution;
    private readonly IOptions<NativeComparisonExecutionOptions> executionOptions;
    private readonly Action<string>? progress;

    /// <summary>Creates a runner for the unchanged materialized control workload.</summary>
    /// <param name="options">Validated bounded control settings.</param>
    /// <param name="executionOptions">Native process observation and progress policy.</param>
    /// <param name="progress">Optional progress observer.</param>
    /// <param name="provider">Borrowed clock; defaults to the system provider.</param>
    public ComparisonRunner(IOptions<ComparisonOptions> options, IOptions<NativeComparisonExecutionOptions> executionOptions, Action<string>? progress = null, TimeProvider? provider = null)
    {
        timeProvider = provider ?? TimeProvider.System;
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.options!.Validate();
        workloadOptions = options;
        this.executionOptions = NativeComparisonExecutionOptions.Require(executionOptions);
        this.progress = progress;
    }

    private ComparisonRunner(ScaledComparisonProfile scaledProfile, IOptions<NativeComparisonExecutionOptions> executionOptions, Action<string>? progress, TimeProvider? provider = null)
    {
        timeProvider = provider ?? TimeProvider.System;
        this.scaledProfile = scaledProfile;
        this.executionOptions = NativeComparisonExecutionOptions.Require(executionOptions);
        this.progress = progress;
    }

    private ComparisonRunner(VectorComparisonProfile vectorProfile, IOptions<NativeComparisonExecutionOptions> executionOptions, Action<string>? progress, TimeProvider? provider = null)
    {
        timeProvider = provider ?? TimeProvider.System;
        this.vectorProfile = vectorProfile;
        vectorExecution = executionOptions;
        this.executionOptions = NativeComparisonExecutionOptions.Require(executionOptions);
        this.progress = progress;
    }

    /// <summary>Creates a runner for one exact typed scaled profile without manufacturing control options!.</summary>
    /// <param name="profile">The closed accepted scaled profile.</param>
    /// <param name="executionOptions">Native process observation and progress policy.</param>
    /// <param name="progress">Optional progress observer.</param>
    /// <param name="provider">Borrowed clock; defaults to the system provider.</param>
    /// <returns>The shared comparison runner with its bounded scale path selected.</returns>
    public static ComparisonRunner ForScaled(ScaledComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions, Action<string>? progress = null, TimeProvider? provider = null)
        => new(profile ?? throw new ArgumentNullException(nameof(profile)), executionOptions, progress, provider);

    /// <summary>Creates a runner for one exact native vector profile.</summary>
    /// <param name="profile">The immutable bounded vector profile.</param>
    /// <param name="executionOptions">The explicitly configured native execution limits.</param>
    /// <param name="progress">The optional progress observer.</param>
    /// <param name="provider">Borrowed clock; defaults to the system provider.</param>
    /// <returns>The native vector workload runner.</returns>
    public static ComparisonRunner ForVector(VectorComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions, Action<string>? progress = null, TimeProvider? provider = null)
        => new(profile ?? throw new ArgumentNullException(nameof(profile)), executionOptions ?? throw new ArgumentNullException(nameof(executionOptions)), progress, provider);

    /// <summary>Runs the vector-specific workload against its already Aspire-owned native target.</summary>
    /// <param name="target">The actual native vector target.</param>
    /// <param name="sourceRevision">The measured source revision.</param>
    /// <param name="cancellationToken">Cancels native execution.</param>
    /// <param name="storage">The observed storage profile.</param>
    /// <returns>The measured native vector report.</returns>
    public Task<ComparisonReport> RunAsync(IVectorComparisonTarget target, string? sourceRevision,
        CancellationToken cancellationToken, string storage = UnrecordedStorage)
    {
        if (vectorProfile is null || scaledProfile is not null || options is not null)
        {
            throw new InvalidOperationException(ComparisonRunnerValues.AVectorProfileMustBeSelected);
        }
        return new VectorComparisonRunner(vectorProfile, vectorExecution!, timeProvider).RunAsync(target, sourceRevision, storage, cancellationToken);
    }

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
        if (vectorProfile is not null)
        {
            throw new InvalidOperationException(ComparisonRunnerValues.AVectorProfileRequiresTheNative);
        }
        if (scaledProfile is { } profile)
        {
            return await new ScaledComparisonRunner(profile, executionOptions, progress, timeProvider).RunAsync(targets, sourceRevision,
                storage, scenario, cancellationToken).ConfigureAwait(false);
        }
        ValidateTargets(targets, scenario);

        var started = timeProvider.GetUtcNow();
        await using var observer = new ComparisonProgressObserver(progress, executionOptions, timeProvider);
        observer.Begin(ComparisonProgressPhase.Oracle, ComparisonRunnerValues.FirstIndex);
        cancellationToken.ThrowIfCancellationRequested();
        var dataset = new BenchmarkDataset(workloadOptions!);
        var cases = new List<ComparisonCase>();
        PrepareOracle(dataset, scenario, observer, cancellationToken);
        for (var repetition = ComparisonRunnerValues.FirstIndex; repetition < options!.Repetitions; repetition++)
        {
            var offset = repetition % targets.Length;
            foreach (var target in targets.Skip(offset).Concat(targets.Take(offset)))
            {
                await RunTargetAsync(target, dataset, repetition, cases, scenario, observer, cancellationToken);
            }
        }
        observer.Complete();
        return new(ComparisonRunnerValues.ReportSchemaVersion, Guid.NewGuid(), started, options, dataset.Sha256, LoadModel,
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
        if (scenario is not null && targets.Length != ComparisonRunnerValues.SingleElementOffset)
        {
            throw new ArgumentException(IsolatedTargetRequired, nameof(targets));
        }
        if (targets.Length == ComparisonRunnerValues.FirstIndex)
        {
            throw new ArgumentException(ComparisonRunnerValues.AtLeastOneTargetIsRequired, nameof(targets));
        }
    }

    private void PrepareOracle(BenchmarkDataset dataset, Scenario? selectedScenario,
        ComparisonProgressObserver observer, CancellationToken cancellationToken)
    {
        var operations = selectedScenario is null or Scenario.VectorExact or Scenario.GraphNeighbors or Scenario.GraphTraverse
            ? Math.Max(options!.Operations, options!.Warmup) : ComparisonRunnerValues.FirstIndex;
        observer.Begin(ComparisonProgressPhase.Oracle, ComparisonRunnerValues.FirstIndex, operations);
        cancellationToken.ThrowIfCancellationRequested();
        for (var operation = ComparisonRunnerValues.FirstIndex; operation < operations; operation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selectedScenario is null or Scenario.VectorExact)
            {
                dataset.ExactNeighbors(dataset.Input(Scenario.VectorExact, ComparisonRunnerValues.FirstIndex, operation, false));
            }
            if (selectedScenario is null or Scenario.GraphNeighbors or Scenario.GraphTraverse)
            {
                var root = dataset.Input(Scenario.GraphTraverse, ComparisonRunnerValues.FirstIndex, operation, false);
                dataset.Reachable(root, ComparisonRunnerValues.SingleElementOffset);
                cancellationToken.ThrowIfCancellationRequested();
                dataset.Reachable(root, options!.GraphDepth);
            }
            observer.Settle(success: true);
        }
    }

    private async Task RunTargetAsync(IComparisonTarget target, BenchmarkDataset dataset, int repetition,
        List<ComparisonCase> cases, Scenario? selectedScenario, ComparisonProgressObserver observer, CancellationToken cancellationToken)
    {
        var scenarios = selectedScenario is { } selected ? [selected] : Enum.GetValues<Scenario>();
        string? failure = null;
        if (repetition == ComparisonRunnerValues.FirstIndex)
        {
            observer.Begin(ComparisonProgressPhase.Initialize, repetition + ComparisonRunnerValues.SingleElementOffset);
            try
            {
                await target.InitializeAsync(dataset, cancellationToken);
                if (selectedScenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete
                    && target.Supports(Scenario.DocumentUpdate) && target.Supports(Scenario.DocumentDelete))
                {
                    await ComparisonMutationProbe.VerifyAsync(target, dataset, token: cancellationToken, timeProvider: timeProvider);
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
            cases.Add(await RunCaseAsync(target, dataset, scenario, repetition, failure, observer, cancellationToken));
        }
    }

    private async Task<ComparisonCase> RunCaseAsync(IComparisonTarget target, BenchmarkDataset dataset,
        Scenario scenario, int repetition, string? setupFailure, ComparisonProgressObserver observer, CancellationToken cancellationToken)
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

        ComparisonCase result;
        try
        { result = await new ComparisonMeasurer(workloadOptions!, observer, executionOptions, timeProvider).MeasureAsync(target, dataset, scenario, repetition, cancellationToken); }
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

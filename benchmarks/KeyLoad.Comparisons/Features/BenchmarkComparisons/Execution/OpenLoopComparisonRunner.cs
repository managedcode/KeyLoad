using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Executes one separate fixed-arrival measurement over an existing bounded native target.</summary>
/// <param name="profile">The exact immutable workload profile.</param>
/// <param name="offeredRatePerSecond">The selected fixed arrival rate.</param>
/// <param name="executionOptions">The validated native execution-policy source.</param>
/// <param name="nativeExecutionOptions">The validated adapter and resource sampling policy.</param>
/// <param name="progress">The optional bounded textual progress sink.</param>
/// <param name="nativeProgress">The optional actual native completion observer.</param>
/// <param name="provider">Borrowed clock; defaults to the system provider.</param>
public sealed class OpenLoopComparisonRunner(ScaledComparisonProfile profile, int offeredRatePerSecond,
    IOptions<OpenLoopExecutionOptions> executionOptions, IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions,
    Action<string>? progress = null,
    Action<OpenLoopProgressV1>? nativeProgress = null, TimeProvider? provider = null)
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const double AccountingSnapshotElapsedSeconds = 1d;

    private readonly OpenLoopExecutionPolicy executionPolicy = OpenLoopExecutionOptions.Snapshot(
        (executionOptions ?? throw new ArgumentNullException(nameof(executionOptions))).Value);

    private readonly IOptions<NativeComparisonExecutionOptions> nativeOptions = NativeComparisonExecutionOptions.Require(nativeExecutionOptions);

    internal OpenLoopExecutionPolicy ExecutionPolicy => executionPolicy;

    /// <summary>Runs one Linux comparison cell without changing the closed-loop report contract.</summary>
    /// <param name="target">The already acquired native comparison target.</param>
    /// <param name="worker">The actual isolated source/run/attempt identity.</param>
    /// <param name="storage">The selected storage disposition.</param>
    /// <param name="cancellationToken">The original caller cancellation token.</param>
    /// <returns>The separate bounded open-loop evidence report.</returns>
    public async Task<OpenLoopComparisonReport> RunAsync(IComparisonTarget target,
        IsolatedComparisonWorker worker, string storage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);
        OpenLoopComparisonValidation.Validate(profile, offeredRatePerSecond, target, worker, executionPolicy);
        var execution = new OpenLoopExecutionContext(profile, offeredRatePerSecond, target, worker, storage, executionPolicy);
        var measurement = RunMeasurementAsync(execution, cancellationToken);
        try
        {
            await measurement.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == cancellationToken
            && cancellationToken.IsCancellationRequested)
        {
            execution.CallerCancelled = true;
            execution.Primary = error;
        }
        catch (Exception error) when (measurement.IsFaulted || measurement.IsCanceled)
        {
            execution.Primary = error;
        }
        finally
        {
            await OpenLoopExecutionCompletion.SettleAsync(execution, timeProvider: timeProvider).ConfigureAwait(false);
        }
        var failure = OpenLoopFailure.Combine(execution.Primary, execution.CleanupFailures);
        OpenLoopExecutionCompletion.ThrowUnlessMeasuredCancellation(failure, execution.State, execution.CallerCancelled, cancellationToken);
        return OpenLoopExecutionCompletion.CreateReport(execution, progress);
    }

    private async Task RunMeasurementAsync(OpenLoopExecutionContext execution, CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;

        await execution.Target.InitializeAsync(execution.Corpus, cancellationToken).ConfigureAwait(false);
        OpenLoopComparisonValidation.ValidateObservedTarget(execution.Worker, execution.Target.Profile);
        var inputs = new ScaledOperationInputs(execution.Corpus, execution.Scenario);
        execution.Sessions.AddRange(await OpenLoopSessionAcquisition.OpenAsync(execution.Target,
            execution.ExecutionPolicy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false));
        await ScaledCorpusReadbackVerifier.VerifyAsync(execution.Sessions[FirstElementIndex], execution.Corpus, cancellationToken)
            .ConfigureAwait(false);
        await ScaledComparisonOperationSetup.PrepareAsync(execution.Sessions, inputs, execution.Profile,
            cancellationToken).ConfigureAwait(false);
        await ScaledComparisonOperationSetup.WarmupAsync(execution.Sessions, inputs, execution.Profile,
            TimeSpan.FromMilliseconds(execution.ExecutionPolicy.OperationDeadlineMilliseconds), token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        StartMeasurement(execution);
        await using var sampler = new ClientResourceSampler(nativeOptions, provider: timeProvider);
        try
        {
            execution.DrainExpired = await MeasureAsync(execution, inputs, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            execution.MeasurementFinishedTimestamp = timeProvider.GetTimestamp();
        }
        cancellationToken.ThrowIfCancellationRequested();
        await VerifyMutationReadbackAsync(execution, inputs, cancellationToken).ConfigureAwait(false);
        execution.Resources = await sampler.StopAsync().ConfigureAwait(false);
    }

    private void StartMeasurement(OpenLoopExecutionContext execution)
    {
        var started = timeProvider.GetTimestamp();
        execution.StartedAt = timeProvider.GetUtcNow();
        execution.Timeline = new(started, execution.Rate, execution.ExecutionPolicy, timeProvider);
        execution.State = new(execution.Timeline, execution.Profile.PayloadBytes, execution.Rate,
            execution.Scenario);
    }

    private static async Task VerifyMutationReadbackAsync(OpenLoopExecutionContext execution,
        ScaledOperationInputs inputs, CancellationToken cancellationToken)
    {
        var state = execution.State ?? throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopMeasurementNotStarted);
        if (state.Snapshot(AccountingSnapshotElapsedSeconds).Accounting.Succeeded != OpenLoopRateContract.PlannedOperations
            || execution.Scenario == Scenario.PointRead)
        {
            return;
        }
        await ScaledComparisonOperationSetup.VerifyMutationResultsAsync(execution.Sessions, inputs,
            execution.Profile, cancellationToken).ConfigureAwait(false);
        execution.Readback = true;
    }

    private Task<bool> MeasureAsync(OpenLoopExecutionContext execution, ScaledOperationInputs inputs,
        CancellationToken cancellationToken)
        => OpenLoopWorkLifetime.RunAsync(execution.Scenario, inputs, execution.Sessions, execution.Timeline,
            execution.State ?? throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopMeasurementNotStarted),
            nativeProgress, cancellationToken: cancellationToken, timeProvider: timeProvider);
}

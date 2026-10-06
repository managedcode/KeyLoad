using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class ScaledComparisonCaseRunner
{
    private const string ExecuteAsyncScaleText = "scale:";
    private const string ExecuteAsyncText = ":";
    private const string ExecuteAsyncMeasureText = ":measure";
    private const int EmptyCount = 0;

    private const string CancelledNativeOperationsWereJoinedDetail = "Cancelled; native operations were joined.";

    private const int RequiredLatencySampleCount = 4_096;
    private const string SampleAlgorithm = "evenly-spaced-operation-indices.v1";
    private const string FailureDetail = "Scaled case failed; latency values are a bounded deterministic sample.";

    internal static async Task<ComparisonCase> RunAsync(IComparisonTarget target, IComparisonCorpus corpus, Scenario scenario, string? setupFailure, Action<string>? progress, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;

        if (!target.Supports(scenario))
        {
            return new(target.Profile.Name, scenario, NoObservedItems, ComparisonStatuses.Unsupported, target.UnsupportedReason, null, []);
        }
        if (setupFailure is not null)
        {
            return Failed(target, scenario, setupFailure, corpus.Settings);
        }

        var inputs = new ScaledOperationInputs(corpus, scenario);
        var settings = corpus.Settings;
        var sessions = await ScaledComparisonOperationSetup.OpenSessionsAsync(target, settings.Concurrency, executionOptions, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var run = await RunSessionsAsync(target: target, scenario: scenario, sessions: sessions, inputs: inputs, settings: settings,
            progress: progress, cancellationToken: cancellationToken, executionOptions: executionOptions, timeProvider: timeProvider).ConfigureAwait(false);
        var result = run.Closed ? run.Result : run.Result with { Status = ComparisonStatuses.Failed, Detail = ComparisonSessionCleanup.Failure };
        if (result.Status == ComparisonStatuses.Failed && !cancellationToken.IsCancellationRequested)
        {
            await ComparisonFailureDiagnostics.ObserveAsync(target, result, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private static async Task<(ComparisonCase Result, bool Closed)> RunSessionsAsync(IComparisonTarget target, Scenario scenario, List<IComparisonSession> sessions, ScaledOperationInputs inputs, IComparisonSettings settings, Action<string>? progress, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ComparisonCase result;
        bool closed;
        try
        {
            result = await ExecuteAsync(target: target, scenario: scenario, sessions: sessions, inputs: inputs, settings: settings,
                progress: progress, cancellationToken: cancellationToken, executionOptions: executionOptions, timeProvider: timeProvider).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = Cancelled(target, scenario, settings);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            result = Failed(target, scenario, ComparisonErrors.Safe(error), settings);
        }
        finally
        {
            closed = await ComparisonSessionCleanup.CloseAsync(sessions, executionOptions.Value.CleanupTimeout, timeProvider: timeProvider).ConfigureAwait(false);
        }
        return (result, closed);
    }

    private static async Task<ComparisonCase> ExecuteAsync(IComparisonTarget target, Scenario scenario, List<IComparisonSession> sessions, ScaledOperationInputs inputs, IComparisonSettings settings, Action<string>? progress, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        await ScaledComparisonOperationSetup.PrepareAsync(sessions, inputs, settings, cancellationToken).ConfigureAwait(false);
        await ScaledComparisonOperationSetup.WarmupAsync(sessions, inputs, settings, executionOptions.Value.OperationTimeout, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        progress?.Invoke($"{ExecuteAsyncScaleText}{target.Profile.Name}{ExecuteAsyncText}{scenario}{ExecuteAsyncMeasureText}");
        var measured = await ScaledComparisonMeasurementExecutor.MeasureAsync(sessions: sessions, inputs: inputs, settings: settings,
            token: cancellationToken, executionOptions: executionOptions, timeProvider: timeProvider).ConfigureAwait(false);
        var result = CreateCase(target, scenario, settings, measured, cancellationToken.IsCancellationRequested);
        var validationFailure = await ValidateMutationResultsAsync(sessions, inputs, settings, cancellationToken).ConfigureAwait(false);
        return validationFailure is null ? result : result with { Status = ComparisonStatuses.Failed, Detail = validationFailure };
    }

    private static async Task<string?> ValidateMutationResultsAsync(List<IComparisonSession> sessions,
        ScaledOperationInputs inputs, IComparisonSettings settings, CancellationToken cancellationToken)
    {
        const string CancelledDuringUntimedMutationReadbackDetail = "Cancelled during untimed mutation readback.";

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        try
        {
            await ScaledComparisonOperationSetup.VerifyMutationResultsAsync(sessions, inputs, settings, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CancelledDuringUntimedMutationReadbackDetail;
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            return ComparisonErrors.Safe(error);
        }
    }

    private static ComparisonCase CreateCase(IComparisonTarget target, Scenario scenario, IComparisonSettings settings,
        ScaledMeasurementResult measured, bool cancelled)
    {
        const int NoItems = 0;
        const int NoMeasuredRate = 0;
        const int NoObservedItems = 0;

        var measurement = measured.Samples.Length == NoItems ? null
            : ComparisonStatistics.Summarize(measured.Samples, measured.ElapsedSeconds) with
            {
                Attempts = measured.Attempted,
                Successes = measured.Successes,
                Failures = measured.Failures,
                UsefulOperationsPerSecond = measured.ElapsedSeconds > NoMeasuredRate ? measured.Successes / measured.ElapsedSeconds : NoMeasuredRate,
                ClientResources = measured.ClientResources
            };
        var accounting = new ScaledOperationAccounting(settings.Operations, measured.Attempted,
            measured.Successes, measured.Failures, measured.DeadlineTimeouts, measured.Rejections,
            settings.Operations - measured.Attempted, SampleAlgorithm, RequiredLatencySampleCount,
            measured.Samples.Length, RequiredLatencySampleCount - measured.Samples.Length);
        var failed = measured.Failures != NoObservedItems || accounting.Unfinished != NoObservedItems || accounting.MissingSamples != NoObservedItems || cancelled;
        return new(target.Profile.Name, scenario, NoObservedItems, failed ? ComparisonStatuses.Failed : ComparisonStatuses.Measured,
            failed ? FailureDetail : null, measurement, measured.Samples.ToImmutableArray())
        { Scaled = accounting };
    }

    private static ComparisonCase Failed(IComparisonTarget target, Scenario scenario, string detail,
        IComparisonSettings settings) => WithNoAttempts(target, scenario, settings, detail);

    private static ComparisonCase Cancelled(IComparisonTarget target, Scenario scenario,
        IComparisonSettings settings) => WithNoAttempts(target, scenario, settings, CancelledNativeOperationsWereJoinedDetail);

    private static ComparisonCase WithNoAttempts(IComparisonTarget target, Scenario scenario,
        IComparisonSettings settings, string detail)
    {
        const int NoObservedItems = 0;

        var accounting = new ScaledOperationAccounting(settings.Operations, NoObservedItems, NoObservedItems, NoObservedItems, EmptyCount, NoObservedItems,
            settings.Operations, SampleAlgorithm, RequiredLatencySampleCount, NoObservedItems, RequiredLatencySampleCount);
        return new(target.Profile.Name, scenario, NoObservedItems, ComparisonStatuses.Failed, detail, null, []) { Scaled = accounting };
    }
}

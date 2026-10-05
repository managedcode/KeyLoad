using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal static class ScaledComparisonCaseRunner
{
    private const int SampleCapacity = 4_096;
    private const string SampleAlgorithm = "evenly-spaced-operation-indices.v1";
    private const string FailureDetail = "Scaled case failed; latency values are a bounded deterministic sample.";

    internal static async Task<ComparisonCase> RunAsync(IComparisonTarget target, IComparisonCorpus corpus,
        Scenario scenario, string? setupFailure, Action<string>? progress, CancellationToken cancellationToken)
    {
        if (!target.Supports(scenario))
        {
            return new(target.Profile.Name, scenario, 0, ComparisonStatuses.Unsupported, target.UnsupportedReason, null, []);
        }
        if (setupFailure is not null)
        {
            return Failed(target, scenario, setupFailure, corpus.Settings);
        }

        var inputs = new ScaledOperationInputs(corpus, scenario);
        var settings = corpus.Settings;
        var sessions = await ScaledComparisonOperationSetup.OpenSessionsAsync(target, settings.Concurrency, cancellationToken).ConfigureAwait(false);
        var run = await RunSessionsAsync(target, scenario, sessions, inputs, settings, progress, cancellationToken).ConfigureAwait(false);
        var result = run.Closed ? run.Result : run.Result with { Status = ComparisonStatuses.Failed, Detail = ComparisonSessionCleanup.Failure };
        if (result.Status == ComparisonStatuses.Failed && !cancellationToken.IsCancellationRequested)
        {
            await ComparisonFailureDiagnostics.ObserveAsync(target, result, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private static async Task<(ComparisonCase Result, bool Closed)> RunSessionsAsync(IComparisonTarget target,
        Scenario scenario, List<IComparisonSession> sessions, ScaledOperationInputs inputs,
        IComparisonSettings settings, Action<string>? progress, CancellationToken cancellationToken)
    {
        ComparisonCase result;
        bool closed;
        try
        {
            result = await ExecuteAsync(target, scenario, sessions, inputs, settings, progress, cancellationToken).ConfigureAwait(false);
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
            closed = await ComparisonSessionCleanup.CloseAsync(sessions, settings.TimeoutSeconds).ConfigureAwait(false);
        }
        return (result, closed);
    }

    private static async Task<ComparisonCase> ExecuteAsync(IComparisonTarget target, Scenario scenario,
        List<IComparisonSession> sessions, ScaledOperationInputs inputs, IComparisonSettings settings,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        await ScaledComparisonOperationSetup.PrepareAsync(sessions, inputs, settings, cancellationToken).ConfigureAwait(false);
        await ScaledComparisonOperationSetup.WarmupAsync(sessions, inputs, settings, cancellationToken).ConfigureAwait(false);
        progress?.Invoke($"scale:{target.Profile.Name}:{scenario}:measure");
        var measured = await ScaledComparisonMeasurementExecutor.MeasureAsync(sessions, inputs, settings, cancellationToken).ConfigureAwait(false);
        var result = CreateCase(target, scenario, settings, measured, cancellationToken.IsCancellationRequested);
        var validationFailure = await ValidateMutationResultsAsync(sessions, inputs, settings, cancellationToken).ConfigureAwait(false);
        return validationFailure is null ? result : result with { Status = ComparisonStatuses.Failed, Detail = validationFailure };
    }

    private static async Task<string?> ValidateMutationResultsAsync(List<IComparisonSession> sessions,
        ScaledOperationInputs inputs, IComparisonSettings settings, CancellationToken cancellationToken)
    {
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
            return "Cancelled during untimed mutation readback.";
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            return ComparisonErrors.Safe(error);
        }
    }

    private static ComparisonCase CreateCase(IComparisonTarget target, Scenario scenario, IComparisonSettings settings,
        ScaledMeasurementResult measured, bool cancelled)
    {
        var measurement = measured.Samples.Length == 0 ? null
            : ComparisonStatistics.Summarize(measured.Samples, measured.ElapsedSeconds) with
            {
                Attempts = measured.Attempted,
                Successes = measured.Successes,
                Failures = measured.Failures,
                UsefulOperationsPerSecond = measured.ElapsedSeconds > 0 ? measured.Successes / measured.ElapsedSeconds : 0,
                ClientResources = measured.ClientResources
            };
        var accounting = new ScaledOperationAccounting(settings.Operations, measured.Attempted,
            measured.Successes, measured.Failures, measured.DeadlineTimeouts, measured.Rejections,
            settings.Operations - measured.Attempted, SampleAlgorithm, SampleCapacity,
            measured.Samples.Length, SampleCapacity - measured.Samples.Length);
        var failed = measured.Failures != 0 || accounting.Unfinished != 0 || accounting.MissingSamples != 0 || cancelled;
        return new(target.Profile.Name, scenario, 0, failed ? ComparisonStatuses.Failed : ComparisonStatuses.Measured,
            failed ? FailureDetail : null, measurement, measured.Samples.ToImmutableArray()) { Scaled = accounting };
    }

    private static ComparisonCase Failed(IComparisonTarget target, Scenario scenario, string detail,
        IComparisonSettings settings) => WithNoAttempts(target, scenario, settings, detail);

    private static ComparisonCase Cancelled(IComparisonTarget target, Scenario scenario,
        IComparisonSettings settings) => WithNoAttempts(target, scenario, settings, "Cancelled; native operations were joined.");

    private static ComparisonCase WithNoAttempts(IComparisonTarget target, Scenario scenario,
        IComparisonSettings settings, string detail)
    {
        var accounting = new ScaledOperationAccounting(settings.Operations, 0, 0, 0, 0, 0,
            settings.Operations, SampleAlgorithm, SampleCapacity, 0, SampleCapacity);
        return new(target.Profile.Name, scenario, 0, ComparisonStatuses.Failed, detail, null, []) { Scaled = accounting };
    }
}

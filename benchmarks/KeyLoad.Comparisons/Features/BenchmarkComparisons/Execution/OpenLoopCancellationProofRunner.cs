using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Runs one actual bounded KeyLoad cancellation proof and verifies a post-cancel SDK read.</summary>
public static class OpenLoopCancellationProofRunner
{
    /// <summary>Runs the frozen RF3 proof through the existing target and fixed-rate runner.</summary>
    /// <param name="profile">The selected immutable scale profile.</param>
    /// <param name="rate">The exact selected offered rate.</param>
    /// <param name="target">The existing initialized-once native comparison target.</param>
    /// <param name="worker">The actual isolated source/run/attempt identity.</param>
    /// <param name="storage">The comparison storage disposition.</param>
    /// <param name="output">The owned output directory holding the fixed control and proof files.</param>
    /// <param name="emitMarker">The native Aspire log sink for the bounded marker.</param>
    /// <param name="executionOptions">The single validated native execution-options source.</param>
    /// <param name="nativeExecutionOptions">The actual native adapter and resource observation policy.</param>
    /// <param name="hostToken">The uncancelled host token used for the healthy read and proof write.</param>
    /// <returns>The validated proof after original work and the healthy-read session settle.</returns>
    public static async Task<OpenLoopCancellationProofV1> RunAsync(ScaledComparisonProfile profile,
        int rate, IComparisonTarget target, IsolatedComparisonWorker worker, string storage, string output,
        Action<string> emitMarker, IOptions<OpenLoopExecutionOptions> executionOptions,
        IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions,
        CancellationToken hostToken)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(nativeExecutionOptions);
        ArgumentNullException.ThrowIfNull(target);
        OpenLoopProgressV1? milestone = null;
        Action<OpenLoopProgressV1> publish = progress => PublishFirstMilestone(profile, progress,
            emitMarker, ref milestone);
        var runner = new OpenLoopComparisonRunner(profile, rate, executionOptions, nativeExecutionOptions,
            nativeProgress: publish);
        var report = await RunRequestedMeasurementAsync(profile, rate, target, worker, storage,
            output, emitMarker, runner, hostToken).ConfigureAwait(false);
        var actualMilestone = milestone ?? throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationMilestoneMissing);
        var corpus = new ScaledComparisonCorpus(profile);
        var health = await OpenLoopHealthyReadVerifier.ReadAndVerifyAsync(target, corpus, hostToken)
            .ConfigureAwait(false);
        var proof = CreateProof(report, actualMilestone, health);
        _ = await OpenLoopCancellationProofWriter.WriteAsync(output, proof, hostToken).ConfigureAwait(false);
        return proof;
    }

    private static async Task<OpenLoopComparisonReport> RunRequestedMeasurementAsync(
        ScaledComparisonProfile profile, int rate, IComparisonTarget target, IsolatedComparisonWorker worker,
        string storage, string output, Action<string> emitMarker, OpenLoopComparisonRunner runner,
        CancellationToken hostToken)
    {
        var policy = runner.ExecutionPolicy;
        ValidateArguments(profile, rate, target, worker, storage, output, emitMarker, policy);
        OpenLoopCancellationRequestWatcher.ValidateFresh(output);
        using var runnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        using var watcherStop = new CancellationTokenSource();
        return await RunAndSettleMeasurementAsync(target, worker, storage, output, runner, policy,
            runnerCancellation, watcherStop, hostToken).ConfigureAwait(false);
    }

    private static async Task<OpenLoopComparisonReport> RunAndSettleMeasurementAsync(
        IComparisonTarget target, IsolatedComparisonWorker worker, string storage, string output,
        OpenLoopComparisonRunner runner, OpenLoopExecutionPolicy policy,
        CancellationTokenSource runnerCancellation, CancellationTokenSource watcherStop,
        CancellationToken hostToken)
    {
        var watcher = OpenLoopCancellationRequestWatcher.WatchAndCancelAsync(output, runnerCancellation,
            policy, watcherStop.Token);
        var measurement = runner.RunAsync(target, worker, storage, runnerCancellation.Token);
        OpenLoopComparisonReport? report = null;
        Exception? primary = null;
        try
        {
            report = await measurement.ConfigureAwait(false);
        }
        catch (Exception error) when (measurement.IsFaulted || measurement.IsCanceled)
        {
            primary = error;
        }
        var failures = ImmutableArray.CreateBuilder<Exception>();
        if (primary is not null)
        {
            failures.Add(primary);
        }
        var requestObserved = await StopAndJoinWatcherAsync(watcherStop, watcher, failures)
            .ConfigureAwait(false);
        return CompleteCancellationMeasurement(report, requestObserved, runnerCancellation,
            failures.ToImmutable(), hostToken);
    }

    private static async Task<bool> StopAndJoinWatcherAsync(CancellationTokenSource watcherStop,
        Task<bool> watcher, ImmutableArray<Exception>.Builder failures)
    {
        var watcherCancellationFailure = await OpenLoopOwnerCancellation.CancelAsync(watcherStop).ConfigureAwait(false);
        if (watcherCancellationFailure is not null)
        {
            failures.Add(watcherCancellationFailure);
        }
        var requestObserved = false;
        try
        {
            requestObserved = await watcher.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == watcherStop.Token
            && watcherStop.IsCancellationRequested)
        {
        }
        catch (Exception error) when (watcher.IsFaulted || watcher.IsCanceled)
        {
            failures.Add(error);
        }
        return requestObserved;
    }

    private static OpenLoopComparisonReport CompleteCancellationMeasurement(OpenLoopComparisonReport? report,
        bool requestObserved, CancellationTokenSource runnerCancellation, ImmutableArray<Exception> failures,
        CancellationToken hostToken)
    {
        var failure = OpenLoopFailure.Combine(null, failures);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        hostToken.ThrowIfCancellationRequested();
        if (!requestObserved || report is null || !runnerCancellation.IsCancellationRequested
            || !report.CallerCancelled)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationWasNotObserved);
        }
        return report;
    }

    private static void ValidateArguments(ScaledComparisonProfile profile, int rate, IComparisonTarget target,
        IsolatedComparisonWorker worker, string storage, string output, Action<string> emitMarker,
        OpenLoopExecutionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(emitMarker);
        if (!policy.IsQualifiedV1() || ScaledComparisonProfileParser.Parse(profile.Id) != profile
            || worker.Scenario != Scenario.PointRead || worker.Target != OpenLoopProtocolIdentities.KeyLoadTarget || worker.NodeCount != policy.MaximumNodes
            || worker.Profile != profile.Id || profile.Operations != OpenLoopRateContract.PlannedOperations
            || !OpenLoopRateContract.AcceptedRates.Contains(rate) || target.Profile.Name != OpenLoopProtocolIdentities.KeyLoadTarget
            || !target.Supports(Scenario.PointRead))
        {
            throw new ArgumentOutOfRangeException(nameof(worker), OpenLoopFailureMessages.CancellationProofCellUnsupported);
        }
    }

    private static void PublishFirstMilestone(ScaledComparisonProfile profile, OpenLoopProgressV1 progress,
        Action<string> emitMarker, ref OpenLoopProgressV1? milestone)
    {
        if (Interlocked.CompareExchange(ref milestone, progress, null) is null)
        {
            emitMarker(OpenLoopNativeCompletionMarker.Format(progress, profile.Id));
        }
    }

    private static OpenLoopCancellationProofV1 CreateProof(OpenLoopComparisonReport report,
        OpenLoopProgressV1 milestone, OpenLoopHealthyReadResult health)
        => new(OpenLoopCancellationProofContract.SchemaVersion, report.Worker!, report.ProfileId, report.OfferedRatePerSecond, report.Scenario,
            report.DatasetRecords, report.DatasetSha256, milestone, report.Accounting, report.ExecutionPolicy,
            report.CallerCancelled,
            true, true, report.SessionsClosed, true, health.Revision, health.JsonSha256, health.SessionClosed);
}

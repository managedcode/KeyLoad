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
    /// <param name="hostToken">The uncancelled host token used for the healthy read and proof write.</param>
    /// <returns>The validated proof after original work and the healthy-read session settle.</returns>
    public static async Task<OpenLoopCancellationProofV1> RunAsync(ScaledComparisonProfile profile,
        int rate, IComparisonTarget target, IsolatedComparisonWorker worker, string storage, string output,
        Action<string> emitMarker, IOptions<OpenLoopExecutionOptions> executionOptions,
        CancellationToken hostToken)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        OpenLoopProgressV1? milestone = null;
        Action<OpenLoopProgressV1> publish = progress => PublishFirstMilestone(profile, progress,
            emitMarker, ref milestone);
        var runner = new OpenLoopComparisonRunner(profile, rate, executionOptions, nativeProgress: publish);
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
        var watcher = OpenLoopCancellationRequestWatcher.WatchAndCancelAsync(output, runnerCancellation,
            policy, watcherStop.Token);
        var report = await RunAndSettleWatcherAsync(runner, target, worker, storage, runnerCancellation,
            watcherStop, watcher, hostToken).ConfigureAwait(false);
        return report;
    }

    private static async Task<OpenLoopComparisonReport> RunAndSettleWatcherAsync(
        OpenLoopComparisonRunner runner, IComparisonTarget target, IsolatedComparisonWorker worker,
        string storage, CancellationTokenSource runnerCancellation, CancellationTokenSource watcherStop,
        Task<bool> watcher, CancellationToken hostToken)
    {
        OpenLoopComparisonReport? report = null;
        Exception? primary = null;
        try
        {
            report = await runner.RunAsync(target, worker, storage, runnerCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = error;
        }
        var settlement = await StopAndJoinWatcherAsync(watcherStop, watcher).ConfigureAwait(false);
        var failure = OpenLoopFailure.Combine(primary, settlement.Failures);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        hostToken.ThrowIfCancellationRequested();
        if (!settlement.RequestObserved || report is null || !runnerCancellation.IsCancellationRequested
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

    private static async Task<OpenLoopWatcherSettlement> StopAndJoinWatcherAsync(
        CancellationTokenSource watcherStop, Task<bool> watcher)
    {
        var failures = ImmutableArray.CreateBuilder<Exception>();
        var cancellationFailure = await OpenLoopOwnerCancellation.CancelAsync(watcherStop).ConfigureAwait(false);
        if (cancellationFailure is not null) failures.Add(cancellationFailure);
        var observed = false;
        try
        {
            observed = await watcher.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == watcherStop.Token
            && watcherStop.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        return new(observed, failures.ToImmutable());
    }

    private static OpenLoopCancellationProofV1 CreateProof(OpenLoopComparisonReport report,
        OpenLoopProgressV1 milestone, OpenLoopHealthyReadResult health)
        => new(OpenLoopCancellationProofContract.SchemaVersion, report.Worker!, report.ProfileId, report.OfferedRatePerSecond, report.Scenario,
            report.DatasetRecords, report.DatasetSha256, milestone, report.Accounting, report.ExecutionPolicy,
            report.CallerCancelled,
            true, true, report.SessionsClosed, true, health.Revision, health.JsonSha256, health.SessionClosed);
}

internal sealed record OpenLoopWatcherSettlement(bool RequestObserved, ImmutableArray<Exception> Failures);

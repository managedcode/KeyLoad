using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Comparisons;

internal static class OpenLoopWorker
{
    internal static async Task RunAsync(IComparisonSession session, int sessionIndex, Scenario scenario,
        ScaledOperationInputs inputs, ChannelReader<OpenLoopWorkItem> reader, OpenLoopTimeline timeline,
        OpenLoopRunState state, CancellationTokenSource lifetime, Action<OpenLoopProgressV1>? nativeProgress)
    {
        await foreach (var item in reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
        {
            var document = inputs.Create(item.Index, warmup: false);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            if (lifetime.IsCancellationRequested || !state.TryStart(item, sessionIndex, started))
            {
                continue;
            }
            await ExecuteStartedAsync(session, item, scenario, state, document, lifetime, timeline.ExecutionPolicy,
                nativeProgress).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteStartedAsync(IComparisonSession session, OpenLoopWorkItem item,
        Scenario scenario, OpenLoopRunState state, BenchmarkDocument document, CancellationTokenSource lifetime,
        OpenLoopExecutionPolicy policy, Action<OpenLoopProgressV1>? nativeProgress)
    {
        var observation = await ObserveAsync(session, item, scenario, document, lifetime, policy)
            .ConfigureAwait(false);
        var progress = state.Complete(item, observation.Outcome, observation.FinishedTimestamp,
            nativeProgress is not null);
        await PublishProgressAsync(progress, nativeProgress, observation.Fatal, lifetime).ConfigureAwait(false);
    }

    private static async Task<OpenLoopNativeObservation> ObserveAsync(IComparisonSession session,
        OpenLoopWorkItem item, Scenario scenario, BenchmarkDocument document,
        CancellationTokenSource lifetime, OpenLoopExecutionPolicy policy)
    {
        using var deadline = CreateDeadline(item, lifetime.Token, policy);
        OpenLoopOutcome outcome;
        Exception? fatal = null;
        long finished;
        try
        {
            var result = await ExecuteAsync(session, scenario, document, deadline.Token).ConfigureAwait(false);
            finished = System.Diagnostics.Stopwatch.GetTimestamp();
            if (finished >= item.DeadlineTimestamp)
            {
                outcome = OpenLoopOutcome.TimedOutAfterStart;
            }
            else if (result.Disposition == OpenLoopSessionDisposition.TargetRejected)
            {
                outcome = OpenLoopOutcome.TargetRejected;
            }
            else
            {
                ScaledComparisonMeasurementExecutor.ValidatePointRead(scenario, result.Result!, document);
                outcome = OpenLoopOutcome.Succeeded;
            }
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested
            && !lifetime.IsCancellationRequested && error.CancellationToken == deadline.Token)
        {
            outcome = OpenLoopOutcome.TimedOutAfterStart;
            finished = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        catch (OperationCanceledException error) when (lifetime.IsCancellationRequested
            && error.CancellationToken == deadline.Token)
        {
            throw new OperationCanceledException(error.Message, error, lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            outcome = OpenLoopOutcome.Failed;
            finished = System.Diagnostics.Stopwatch.GetTimestamp();
            fatal = CqrsRuntimeFailures.FindFatal(error);
        }
        return new(outcome, finished, fatal);
    }

    private static async Task PublishProgressAsync(OpenLoopProgressV1? progress,
        Action<OpenLoopProgressV1>? nativeProgress, Exception? fatal, CancellationTokenSource lifetime)
    {
        Exception? observerFailure = null;
        try
        {
            if (progress is not null)
            {
                nativeProgress?.Invoke(progress);
            }
        }
        catch (Exception error)
        {
            observerFailure = error;
        }

        if (fatal is not null)
        {
            var cancellationFailure = await OpenLoopOwnerCancellation.CancelAsync(lifetime).ConfigureAwait(false);
            var failures = ImmutableArray.CreateBuilder<Exception>();
            if (observerFailure is not null) failures.Add(observerFailure);
            if (cancellationFailure is not null) failures.Add(cancellationFailure);
            ExceptionDispatchInfo.Capture(OpenLoopFailure.Combine(fatal, failures.ToImmutable())!).Throw();
        }
        if (observerFailure is not null)
        {
            var cancellationFailure = await OpenLoopOwnerCancellation.CancelAsync(lifetime).ConfigureAwait(false);
            var failures = cancellationFailure is null
                ? ImmutableArray.Create(observerFailure)
                : ImmutableArray.Create(observerFailure, cancellationFailure);
            ExceptionDispatchInfo.Capture(OpenLoopFailure.Combine(observerFailure, failures)!).Throw();
        }
    }

    private sealed record OpenLoopNativeObservation(OpenLoopOutcome Outcome, long FinishedTimestamp,
        Exception? Fatal);

    private static async Task<OpenLoopSessionResult> ExecuteAsync(IComparisonSession session,
        Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
    {
        if (session is IOpenLoopComparisonSession native)
        {
            return await native.ExecuteOpenLoopAsync(scenario, document, cancellationToken).ConfigureAwait(false);
        }
        var result = await session.ExecuteAsync(scenario, document, cancellationToken).ConfigureAwait(false);
        return new(OpenLoopSessionDisposition.Succeeded, result);
    }

    private static CancellationTokenSource CreateDeadline(OpenLoopWorkItem item, CancellationToken token,
        OpenLoopExecutionPolicy policy)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        var remaining = System.Diagnostics.Stopwatch.GetElapsedTime(
            System.Diagnostics.Stopwatch.GetTimestamp(), item.DeadlineTimestamp);
        if (remaining <= TimeSpan.Zero)
        {
            deadline.Cancel();
        }
        else
        {
            deadline.CancelAfter(remaining < TimeSpan.FromMilliseconds(policy.OperationDeadlineMilliseconds)
                ? remaining : TimeSpan.FromMilliseconds(policy.OperationDeadlineMilliseconds));
        }
        return deadline;
    }
}

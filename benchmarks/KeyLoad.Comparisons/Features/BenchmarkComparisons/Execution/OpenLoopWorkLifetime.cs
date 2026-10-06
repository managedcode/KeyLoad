using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons;

internal static class OpenLoopWorkLifetime
{
    internal static async Task<bool> RunAsync(Scenario scenario, ScaledOperationInputs inputs, List<IComparisonSession> sessions, OpenLoopTimeline timeline, OpenLoopRunState state, Action<OpenLoopProgressV1>? nativeProgress, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return await RunOwnedAsync(scenario, inputs, sessions, timeline, state, lifetime, nativeProgress, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
    }

    private static async Task<bool> RunOwnedAsync(Scenario scenario, ScaledOperationInputs inputs, List<IComparisonSession> sessions, OpenLoopTimeline timeline, OpenLoopRunState state, CancellationTokenSource lifetime, Action<OpenLoopProgressV1>? nativeProgress, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var channel = OpenLoopChannel.Create(timeline.ExecutionPolicy.QueueCapacity);
        var workers = sessions.Select((session, index) => OpenLoopWorker.RunAsync(session, index, scenario,
            inputs, channel.Reader, timeline, state, lifetime, nativeProgress, timeProvider: timeProvider)).ToArray();
        var producer = OpenLoopProducer.RunAsync(inputs, channel.Writer, timeline, state, cancellationToken: lifetime.Token, timeProvider: timeProvider);
        Exception? failure = null;
        var drainExpired = false;
        var scheduling = AwaitScheduleAndDrainAsync(producer, workers, timeline, state, lifetime, cancellationToken: cancellationToken, timeProvider: timeProvider);
        try
        {
            drainExpired = await scheduling.ConfigureAwait(false);
        }
        catch (Exception error) when (scheduling.IsFaulted || scheduling.IsCanceled)
        {
            failure = await FreezeAndCancelAsync(error, state, lifetime, drainExpired, callerToken: cancellationToken, timeProvider: timeProvider)
                .ConfigureAwait(false);
        }
        finally
        {
            channel.Writer.TryComplete();
        }
        failure = await JoinWorkAsync(producer, workers, failure, lifetime, drainExpired, cancellationToken)
            .ConfigureAwait(false);
        failure = OpenLoopFailure.Combine(failure, ImmutableArray<Exception>.Empty);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return drainExpired;
    }

    private static async Task<bool> AwaitScheduleAndDrainAsync(Task producer, Task[] workers, OpenLoopTimeline timeline, OpenLoopRunState state, CancellationTokenSource lifetime, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        await CompleteProducerAsync(producer, workers).ConfigureAwait(false);
        var workersJoined = Task.WhenAll(workers);
        using var drainTimer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var drainWait = timeline.WaitUntilAsync(timeline.DrainDeadline(), drainTimer.Token);
        if (await Task.WhenAny(workersJoined, drainWait).ConfigureAwait(false) == drainWait)
        {
            try
            {
                await drainWait.ConfigureAwait(false);
            }
            catch (OperationCanceledException error) when (error.CancellationToken == drainTimer.Token
                && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(error.Message, error, lifetime.Token);
            }
            state.Freeze(timeProvider.GetTimestamp());
            await lifetime.CancelAsync().ConfigureAwait(false);
            return true;
        }
        Exception? workerFailure = null;
        try
        {
            await workersJoined.ConfigureAwait(false);
        }
        catch (Exception error) when (workersJoined.IsFaulted || workersJoined.IsCanceled)
        {
            workerFailure = error;
        }
        await SettleDrainTimerAsync(drainTimer, drainWait, workerFailure).ConfigureAwait(false);
        return false;
    }

    private static async Task SettleDrainTimerAsync(CancellationTokenSource drainTimer, Task drainWait,
        Exception? primary)
    {
        var cancellationFailure = await OpenLoopOwnerCancellation.CancelAsync(drainTimer).ConfigureAwait(false);
        Exception? drainFailure = null;
        try
        {
            await drainWait.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == drainTimer.Token
            && drainTimer.IsCancellationRequested)
        {
        }
        catch (Exception error) when (drainWait.IsFaulted || drainWait.IsCanceled)
        {
            drainFailure = error;
        }
        var failures = ImmutableArray.CreateBuilder<Exception>();
        if (cancellationFailure is not null)
        {
            failures.Add(cancellationFailure);
        }
        if (drainFailure is not null)
        {
            failures.Add(drainFailure);
        }
        var settlementFailure = OpenLoopFailure.Combine(primary, failures.ToImmutable());
        if (settlementFailure is not null)
        {
            ExceptionDispatchInfo.Capture(settlementFailure).Throw();
        }
    }

    private static async Task CompleteProducerAsync(Task producer, Task[] workers)
    {
        const int NoItems = 0;

        var pendingWorkers = workers.ToList();
        while (!producer.IsCompleted && pendingWorkers.Count > NoItems)
        {
            var completed = await Task.WhenAny(pendingWorkers.Append(producer)).ConfigureAwait(false);
            if (ReferenceEquals(completed, producer))
            {
                break;
            }
            pendingWorkers.Remove(completed);
            await completed.ConfigureAwait(false);
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopWorkerStoppedBeforeSchedule);
        }
        await producer.ConfigureAwait(false);
    }

    private static async Task<Exception?> FreezeAndCancelAsync(Exception primary, OpenLoopRunState state, CancellationTokenSource lifetime, bool drainExpired, TimeProvider timeProvider, CancellationToken callerToken)
    {
        state.Freeze(timeProvider.GetTimestamp());
        var expectedCancellation = ExpectedOwnerCancellation(primary, lifetime, drainExpired, callerToken);
        var cancellationFailure = await OpenLoopOwnerCancellation.CancelAsync(lifetime).ConfigureAwait(false);
        if (expectedCancellation)
        {
            return cancellationFailure;
        }
        return cancellationFailure is null
            ? primary
            : new AggregateException(OpenLoopFailureCodes.OpenLoopMeasurementFailed, primary, cancellationFailure);
    }

    private static async Task<Exception?> JoinWorkAsync(Task producer, Task[] workers, Exception? failure,
        CancellationTokenSource owner, bool drainExpired, CancellationToken callerToken)
    {
        var joined = Task.WhenAll(workers.Prepend(producer));
        try
        {
            await joined.ConfigureAwait(false);
        }
        catch (Exception error) when (joined.IsFaulted || joined.IsCanceled)
        {
            AppendJoinedFailure(joined, error, owner, drainExpired, ref failure, callerToken);
        }
        return failure;
    }

    private static void AppendJoinedFailure(Task joined, Exception error, CancellationTokenSource owner,
        bool drainExpired, ref Exception? failure, CancellationToken callerToken)
    {
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;

        var observed = joined.Exception?.Flatten().InnerExceptions ?? [error];
        var ownsSingleCancellation = observed.Count == SingleItemCount
            && ExpectedOwnerCancellation(observed[FirstElementIndex], owner, drainExpired, callerToken);
        var retained = ownsSingleCancellation ? ImmutableArray<Exception>.Empty : observed.ToImmutableArray();
        failure = OpenLoopFailure.Combine(failure, retained);
    }

    private static bool ExpectedOwnerCancellation(Exception failure, CancellationTokenSource owner,
        bool drainExpired, CancellationToken callerToken)
        => failure is OperationCanceledException cancellation
            && cancellation.CancellationToken == owner.Token
            && (callerToken.IsCancellationRequested || drainExpired);
}

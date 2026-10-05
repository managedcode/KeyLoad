using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons;

internal static class OpenLoopWorkLifetime
{
    internal static async Task<bool> RunAsync(Scenario scenario, ScaledOperationInputs inputs,
        List<IComparisonSession> sessions, OpenLoopTimeline timeline, OpenLoopRunState state,
        Action<OpenLoopProgressV1>? nativeProgress, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = OpenLoopChannel.Create(timeline.ExecutionPolicy.QueueCapacity);
        var workers = sessions.Select((session, index) => OpenLoopWorker.RunAsync(session, index, scenario,
            inputs, channel.Reader, timeline, state, lifetime, nativeProgress)).ToArray();
        var producer = OpenLoopProducer.RunAsync(inputs, channel.Writer, timeline, state, lifetime.Token);
        Exception? failure = null;
        var drainExpired = false;
        try
        {
            drainExpired = await AwaitScheduleAndDrainAsync(producer, workers, timeline, state, lifetime,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = await FreezeAndCancelAsync(error, state, lifetime, cancellationToken, drainExpired)
                .ConfigureAwait(false);
        }
        finally
        {
            channel.Writer.TryComplete();
        }
        failure = await JoinWorkAsync(producer, workers, failure, cancellationToken, lifetime.Token, drainExpired)
            .ConfigureAwait(false);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        cancellationToken.ThrowIfCancellationRequested();
        return drainExpired;
    }

    private static async Task<bool> AwaitScheduleAndDrainAsync(Task producer, Task[] workers,
        OpenLoopTimeline timeline, OpenLoopRunState state, CancellationTokenSource lifetime,
        CancellationToken cancellationToken)
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
            state.Freeze(Stopwatch.GetTimestamp());
            await lifetime.CancelAsync().ConfigureAwait(false);
            return true;
        }
        await workersJoined.ConfigureAwait(false);
        await drainTimer.CancelAsync().ConfigureAwait(false);
        try
        {
            await drainWait.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == drainTimer.Token
            && drainTimer.IsCancellationRequested)
        {
        }
        return false;
    }

    private static async Task CompleteProducerAsync(Task producer, Task[] workers)
    {
        var pendingWorkers = workers.ToList();
        while (!producer.IsCompleted && pendingWorkers.Count > 0)
        {
            var completed = await Task.WhenAny(pendingWorkers.Append(producer)).ConfigureAwait(false);
            if (ReferenceEquals(completed, producer)) break;
            pendingWorkers.Remove(completed);
            await completed.ConfigureAwait(false);
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopWorkerStoppedBeforeSchedule);
        }
        await producer.ConfigureAwait(false);
    }

    private static async Task<Exception?> FreezeAndCancelAsync(Exception primary, OpenLoopRunState state,
        CancellationTokenSource lifetime, CancellationToken callerToken, bool drainExpired)
    {
        state.Freeze(Stopwatch.GetTimestamp());
        var expectedCancellation = ExpectedOwnerCancellation(primary, callerToken, lifetime.Token, drainExpired);
        var cancellationFailure = await OpenLoopOwnerCancellation.CancelAsync(lifetime).ConfigureAwait(false);
        if (expectedCancellation) return cancellationFailure;
        return OpenLoopFailure.Combine(primary,
            cancellationFailure is null ? ImmutableArray<Exception>.Empty : [cancellationFailure]);
    }

    private static async Task<Exception?> JoinWorkAsync(Task producer, Task[] workers, Exception? failure,
        CancellationToken callerToken, CancellationToken ownerToken, bool drainExpired)
    {
        var joined = Task.WhenAll(workers.Prepend(producer));
        try
        {
            await joined.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var observed = joined.Exception?.Flatten().InnerExceptions ?? [error];
            var ownsSingleCancellation = observed.Count == 1
                && ExpectedOwnerCancellation(observed[0], callerToken, ownerToken, drainExpired);
            var retained = ownsSingleCancellation ? ImmutableArray<Exception>.Empty : observed.ToImmutableArray();
            failure = OpenLoopFailure.Combine(failure, retained);
        }
        return failure;
    }

    private static bool ExpectedOwnerCancellation(Exception failure, CancellationToken callerToken,
        CancellationToken ownerToken, bool drainExpired)
        => failure is OperationCanceledException cancellation
            && cancellation.CancellationToken == ownerToken
            && (callerToken.IsCancellationRequested || drainExpired);
}

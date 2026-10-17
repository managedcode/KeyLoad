namespace KeyLoad.Client;

internal static class QueueWorkerSettlement
{
    internal static async Task ReapCompletedAsync(QueueWorkerSession session)
    {
        foreach (var active in session.Active().Where(value => value.Completion.IsCompleted))
        {
            await JoinAsync([active.Completion], session).ConfigureAwait(false);
            session.ReleaseJoined(active);
        }
    }

    internal static async Task WaitOneOrStopAsync(QueueWorkerSession session)
    {
        var tasks = session.Active().Select(value => value.Completion).Append(session.Stopped).ToArray();
        await Task.WhenAny(tasks).ConfigureAwait(false);
    }

    internal static async Task WaitHandlersOrStopAsync(QueueWorkerSession session)
    {
        var all = Task.WhenAll(session.Active().Select(value => value.Completion));
        await Task.WhenAny(all, session.Stopped).ConfigureAwait(false);
        if (!all.IsCompleted)
        {
            await CancelAsync(session).ConfigureAwait(false);
        }
        await JoinAsync([all], session).ConfigureAwait(false);
    }

    internal static async Task CancelAndJoinAsync(QueueWorkerSession session, Task renewal)
    {
        await CancelAsync(session).ConfigureAwait(false);
        var active = session.Active();
        await JoinAsync(active.Select(value => value.Completion).Append(renewal), session).ConfigureAwait(false);
        foreach (var value in active)
        {
            session.ReleaseJoined(value);
        }
    }

    internal static async Task CancelAsync(QueueWorkerSession session)
    {
        try
        {
            await session.Lifetime.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception original) when (QueueWorkerFailureLedger.IsNonFatal(original))
        {
            session.Failure(original);
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
        }
        catch (Exception original) when (!QueueWorkerFailureLedger.IsNonFatal(original))
        {
            session.Failure(original);
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
        }
    }

    private static async Task JoinAsync(IEnumerable<Task> tasks, QueueWorkerSession session)
    {
        var original = tasks.ToArray();
        try
        {
            await Task.WhenAll(original).ConfigureAwait(false);
        }
        catch (Exception observedFailure) when (QueueWorkerFailureLedger.IsNonFatal(observedFailure))
        {
            foreach (var failure in original.Where(value => value.IsFaulted)
                .SelectMany(value => value.Exception!.InnerExceptions))
            {
                session.Failure(failure);
            }
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
        }
        catch (Exception observedFailure) when (!QueueWorkerFailureLedger.IsNonFatal(observedFailure))
        {
            foreach (var failure in original.Where(value => value.IsFaulted)
                .SelectMany(value => value.Exception!.InnerExceptions))
            {
                session.Failure(failure);
            }
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
        }
    }
}

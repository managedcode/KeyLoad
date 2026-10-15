namespace KeyLoad.Client;

internal static class QueueWorkerRenewal
{
    internal static async Task RunAsync(KeyLoadClient client, QueueWorkerSession session, TimeProvider clock)
    {
        var owned = new HashSet<Task>();
        try
        {
            while (!session.Lifetime.IsCancellationRequested)
            {
                await Task.Delay(session.Policy.RenewalInterval, clock, session.Lifetime.Token).ConfigureAwait(false);
                await ObserveCompletedAsync(owned, session).ConfigureAwait(false);
                StartRenewals(client, session, owned);
                if (session.Admission().Stopped)
                {
                    await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested)
        {
        }
        catch (Exception original) when (QueueWorkerFailureLedger.IsNonFatal(original))
        {
            session.Failure(original);
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
            await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
        }
        catch (Exception original) when (!QueueWorkerFailureLedger.IsNonFatal(original))
        {
            session.Failure(original);
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
            await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await JoinAsync(owned, session).ConfigureAwait(false);
        }
    }

    private static void StartRenewals(KeyLoadClient client, QueueWorkerSession session, HashSet<Task> owned)
    {
        foreach (var lease in session.Active().Select(value => value.Lease).ToArray())
        {
            if (session.Lifetime.IsCancellationRequested)
            {
                return;
            }
            owned.Add(lease.RenewAsync(client, session));
        }
    }

    private static async Task ObserveCompletedAsync(HashSet<Task> owned, QueueWorkerSession session)
    {
        var completed = owned.Where(value => value.IsCompleted).ToArray();
        await JoinAsync(completed, session).ConfigureAwait(false);
        foreach (var task in completed)
        {
            owned.Remove(task);
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
            await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
        }
        catch (Exception observedFailure) when (!QueueWorkerFailureLedger.IsNonFatal(observedFailure))
        {
            foreach (var failure in original.Where(value => value.IsFaulted)
                .SelectMany(value => value.Exception!.InnerExceptions))
            {
                session.Failure(failure);
            }
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
            await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
        }
    }
}

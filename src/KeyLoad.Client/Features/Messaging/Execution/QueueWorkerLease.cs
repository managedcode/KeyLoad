namespace KeyLoad.Client;

internal sealed class QueueWorkerLease
{
    private readonly Lock gate = new();
    private readonly QueueLaneRef lane;
    private readonly string token;
    private readonly Guid firstRenewalId;
    private Task renewal = Task.CompletedTask;
    private bool ended;
    private bool blocked;
    private int renewals;

    internal QueueWorkerLease(QueueWorkerDeliveryContext context)
    {
        lane = context.Lane;
        token = context.Delivery.Token;
        firstRenewalId = context.FirstRenewalId;
    }

    internal bool Blocked
    {
        get
        {
            lock (gate)
            {
                return blocked;
            }
        }
    }

    internal Task RenewAsync(KeyLoadClient client, QueueWorkerSession session)
    {
        DeliveryCommand command;
        TaskCompletionSource<Task> registered;
        lock (gate)
        {
            if (ended || !renewal.IsCompleted)
            {
                return renewal;
            }
            if (!session.ReserveRenewal())
            {
                blocked = true;
                session.Stop(QueueWorkerStopReason.RenewalBudget);
                return Task.CompletedTask;
            }
            command = new(renewals == QueueWorkerProtocol.NoPriorRenewals ? firstRenewalId : Guid.NewGuid(),
                lane, token, DeliveryAction.Renew, session.Policy.LeaseSeconds);
            renewals++;
            registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            renewal = registered.Task.Unwrap();
        }
        var actual = RenewCoreAsync(client, session, command);
        registered.TrySetResult(actual);
        return renewal;
    }

    private async Task RenewCoreAsync(KeyLoadClient client, QueueWorkerSession session,
        DeliveryCommand command)
    {
        try
        {
            var result = await QueueWorkerCompletion.RunAsync(client, command, session.Policy,
                session.Observe, session.Lifetime.Token).ConfigureAwait(false);
            if (result.IsFailed)
            {
                lock (gate)
                {
                    blocked = true;
                }
                session.Stop(QueueWorkerStopReason.RenewalFailed);
                await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
            }
        }
        catch (Exception original) when (QueueWorkerFailureLedger.IsNonFatal(original))
        {
            lock (gate)
            {
                blocked = true;
            }
            session.Failure(original);
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
            await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
        }
        catch (Exception original) when (!QueueWorkerFailureLedger.IsNonFatal(original))
        {
            lock (gate)
            {
                blocked = true;
            }
            session.Failure(original);
            session.Stop(QueueWorkerStopReason.LifecycleFailed);
            await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
            throw;
        }
    }

    internal Task EndAsync()
    {
        lock (gate)
        {
            ended = true;
            return renewal;
        }
    }
}

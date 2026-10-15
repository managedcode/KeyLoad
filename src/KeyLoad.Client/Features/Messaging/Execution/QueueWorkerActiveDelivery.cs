using System.Text;

namespace KeyLoad.Client;

internal sealed class QueueWorkerActiveDelivery
{
    private QueueWorkerDeliveryContext? context;
    private readonly KeyLoadClient client;
    private readonly QueueWorkerSession session;
    private readonly Func<QueueWorkerDeliveryContext, CancellationToken, Task> handler;

    internal QueueWorkerActiveDelivery(KeyLoadClient client, QueueWorkerSession session, QueueLaneRef lane,
        Delivery actual, Func<QueueWorkerDeliveryContext, CancellationToken, Task> handler)
    {
        this.client = client;
        this.session = session;
        this.handler = handler;
        context = new(lane, actual, Guid.NewGuid(), Guid.NewGuid());
        Identity = QueueWorkerObservations.Identity(actual);
        BodyBytes = checked((long)Encoding.UTF8.GetByteCount(actual.PayloadJson) + Encoding.UTF8.GetByteCount(actual.HeadersJson));
        Lease = new(context);
    }

    internal QueueWorkerClaimIdentity Identity
    {
        get;
    }
    internal long BodyBytes
    {
        get;
    }
    internal QueueWorkerLease Lease
    {
        get;
    }
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal bool Acknowledged
    {
        get; private set;
    }

    internal void Start() => Completion = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        var handlerCompleted = false;
        try
        {
            session.Lifetime.Token.ThrowIfCancellationRequested();
            if (session.Admission().Stopped)
            {
                return;
            }
            await handler(context!, session.Lifetime.Token).ConfigureAwait(false);
            handlerCompleted = true;
            await Lease.EndAsync().ConfigureAwait(false);
            if (Lease.Blocked || session.Admission().Stopped)
            {
                return;
            }
            var command = new DeliveryCommand(context!.AcknowledgementId, context.Lane,
                context.Delivery.Token, DeliveryAction.Ack);
            var result = await QueueWorkerCompletion.RunAsync(client, command, session.Policy,
                session.Observe, session.Lifetime.Token).ConfigureAwait(false);
            Acknowledged = result.IsSuccess;
            if (!Acknowledged)
            {
                session.Stop(QueueWorkerStopReason.AcknowledgementFailed);
                await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException original) when (session.Lifetime.IsCancellationRequested)
        {
            session.Failure(original);
        }
        catch (Exception original) when (QueueWorkerFailureLedger.IsNonFatal(original))
        {
            session.Failure(original);
            session.Stop(handlerCompleted ? QueueWorkerStopReason.LifecycleFailed : QueueWorkerStopReason.HandlerFailed);
            await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
        }
        catch (Exception original) when (!QueueWorkerFailureLedger.IsNonFatal(original))
        {
            session.Failure(original);
            session.Stop(handlerCompleted ? QueueWorkerStopReason.LifecycleFailed : QueueWorkerStopReason.HandlerFailed);
            await QueueWorkerSettlement.CancelAsync(session).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await EndOwnedLeaseAsync().ConfigureAwait(false);
        }
    }

    private async Task EndOwnedLeaseAsync()
    {
        try
        {
            await Lease.EndAsync().ConfigureAwait(false);
        }
        finally
        {
            context = null;
        }
    }

}

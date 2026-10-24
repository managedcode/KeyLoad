using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

internal static class QueueWorkerExecution
{
    internal static async Task<QueueWorkerResult> RunAsync(KeyLoadClient client, QueueLaneRef lane, Guid firstReceive,
        Func<QueueWorkerDeliveryContext, CancellationToken, Task> handler, IOptions<QueueWorkerOptions> configured,
        TimeProvider clock, CancellationToken token)
    {
        using var session = new QueueWorkerSession(configured, token);
        return await RunSessionAsync(client, lane, firstReceive, handler, session, clock).ConfigureAwait(false);
    }

    private static async Task<QueueWorkerResult> RunSessionAsync(KeyLoadClient client, QueueLaneRef lane, Guid firstReceive,
        Func<QueueWorkerDeliveryContext, CancellationToken, Task> handler, QueueWorkerSession session, TimeProvider clock)
    {
        var renewal = QueueWorkerRenewal.RunAsync(client, session, clock);
        var loopExitReason = QueueWorkerStopReason.Cancelled;
        try
        {
            loopExitReason = await LoopAsync(client, lane, firstReceive, handler, session, clock).ConfigureAwait(false);
        }
        catch (OperationCanceledException original) when (session.Lifetime.IsCancellationRequested)
        {
            session.Failure(original);
            session.Stop(QueueWorkerStopReason.Cancelled);
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
        finally
        {
            await QueueWorkerSettlement.CancelAndJoinAsync(session, renewal).ConfigureAwait(false);
        }
        session.ThrowIfFatal();
        return session.Result(loopExitReason);
    }

    private static async Task<QueueWorkerStopReason> LoopAsync(KeyLoadClient client, QueueLaneRef lane, Guid firstReceive,
        Func<QueueWorkerDeliveryContext, CancellationToken, Task> handler, QueueWorkerSession session, TimeProvider clock)
    {
        while (true)
        {
            await QueueWorkerSettlement.ReapCompletedAsync(session).ConfigureAwait(false);
            var available = session.Admission();
            if (available.Stopped)
            {
                return QueueWorkerStopReason.Cancelled;
            }
            if (available.Remaining <= QueueWorkerProtocol.EmptyCapacity || available.Calls >= session.Policy.MaximumReceiveCalls)
            {
                await QueueWorkerSettlement.WaitHandlersOrStopAsync(session).ConfigureAwait(false);
                return available.Remaining <= QueueWorkerProtocol.EmptyCapacity
                    ? QueueWorkerStopReason.DeliveryBudget : QueueWorkerStopReason.ReceiveBudget;
            }
            if (available.Slots <= QueueWorkerProtocol.EmptyCapacity || available.Bytes <= QueueWorkerProtocol.EmptyCapacity)
            {
                await QueueWorkerSettlement.WaitOneOrStopAsync(session).ConfigureAwait(false);
                continue;
            }
            var id = available.Calls == QueueWorkerProtocol.NoPriorReceives ? firstReceive : Guid.NewGuid();
            var request = new ReceiveRequest(id, lane, Math.Min(available.Slots, available.Remaining),
                Math.Min(available.Bytes, session.Policy.MaximumReceiveBytes), session.Policy.LeaseSeconds);
            session.ReceiveStarted();
            var result = await QueueWorkerReceive.RunAsync(client, request, session.Policy,
                session.Observe, session.Lifetime.Token).ConfigureAwait(false);
            if (result.IsFailed)
            {
                session.Stop(QueueWorkerObservations.Unknown(result)
                    ? QueueWorkerStopReason.ReceiveUncertain : QueueWorkerStopReason.ReceiveRefused);
                return QueueWorkerStopReason.ReceiveRefused;
            }
            var page = result.Value;
            var bodyBytes = page.Deliveries.Sum(value => checked((long)Encoding.UTF8.GetByteCount(value.PayloadJson)
                + Encoding.UTF8.GetByteCount(value.HeadersJson)));
            if (page.RequestId != request.RequestId || page.Deliveries.Length > request.MaxMessages
                || bodyBytes > available.Bytes)
            {
                session.KnownUnhandled(page.Deliveries);
                session.Stop(bodyBytes > available.Bytes ? QueueWorkerStopReason.InFlightBytes : QueueWorkerStopReason.RuntimeReplyRejected);
                return QueueWorkerStopReason.RuntimeReplyRejected;
            }
            foreach (var delivery in page.Deliveries)
            {
                var active = new QueueWorkerActiveDelivery(client, session, lane, delivery, handler);
                session.Add(active);
                active.Start();
            }
            if (page.Deliveries.IsEmpty)
            {
                await Task.Delay(session.Policy.EmptyPollDelay, clock, session.Lifetime.Token).ConfigureAwait(false);
            }
        }
    }
}

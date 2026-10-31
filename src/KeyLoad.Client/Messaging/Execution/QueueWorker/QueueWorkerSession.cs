using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

internal sealed class QueueWorkerSession : IDisposable
{
    private readonly CancellationTokenRegistration cancellation;
    private readonly Lock gate = new();
    private readonly List<QueueWorkerActiveDelivery> active = [];
    private readonly List<QueueWorkerOperationObservation> operations = [];
    private readonly List<QueueWorkerClaimIdentity> unacknowledged = [];
    private readonly List<Exception> failures = [];
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private QueueWorkerStopReason? reason;
    private int peakHandlers;
    private int received;
    private int acknowledged;
    private int receives;
    private int renewals;

    internal QueueWorkerSession(IOptions<QueueWorkerOptions> configured, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(configured);
        Policy = configured.Value;
        Lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cancellation = caller.Register(() => Stop(QueueWorkerStopReason.Cancelled));
    }

    internal QueueWorkerOptions Policy
    {
        get;
    }
    internal CancellationTokenSource Lifetime
    {
        get;
    }
    internal Task Stopped => stopped.Task;

    internal (int Slots, int Remaining, int Calls, bool Stopped) Admission()
    {
        lock (gate)
        {
            return (Policy.MaximumConcurrentHandlers - active.Count, Policy.MaximumDeliveries - received,
                receives, reason is not null || Lifetime.IsCancellationRequested);
        }
    }

    internal void Observe(QueueWorkerOperationObservation value)
    {
        lock (gate)
        {
            operations.Add(value);
        }
    }

    internal void ReceiveStarted()
    {
        lock (gate)
        {
            receives++;
        }
    }

    internal void Add(QueueWorkerActiveDelivery delivery)
    {
        lock (gate)
        {
            active.Add(delivery);
            received++;
            peakHandlers = Math.Max(peakHandlers, active.Count);
        }
    }

    internal QueueWorkerActiveDelivery[] Active()
    {
        lock (gate)
        {
            return active.ToArray();
        }
    }

    internal void ReleaseJoined(QueueWorkerActiveDelivery delivery)
    {
        lock (gate)
        {
            if (!active.Remove(delivery))
            {
                return;
            }
            if (delivery.Acknowledged)
            {
                acknowledged++;
            }
            else
            {
                unacknowledged.Add(delivery.Identity);
            }
        }
    }

    internal void KnownUnhandled(IEnumerable<Delivery> deliveries)
    {
        lock (gate)
        {
            unacknowledged.AddRange(deliveries.Select(QueueWorkerObservations.Identity));
            received += deliveries.Count();
        }
    }

    internal bool ReserveRenewal()
    {
        lock (gate)
        {
            if (reason is not null || renewals >= Policy.MaximumRenewalCalls)
            {
                return false;
            }
            renewals++;
            return true;
        }
    }

    internal void Stop(QueueWorkerStopReason value)
    {
        lock (gate)
        {
            reason ??= value;
        }
        stopped.TrySetResult();
    }

    internal void Failure(Exception original)
    {
        lock (gate)
        {
            if (!failures.Contains(original, ReferenceEqualityComparer.Instance))
            {
                failures.Add(original);
            }
        }
    }

    internal void ThrowIfFatal()
    {
        lock (gate)
        {
            QueueWorkerFailureLedger.ThrowIfFatal(failures, operations);
        }
    }

    internal QueueWorkerResult Result(QueueWorkerStopReason loopExitReason)
    {
        lock (gate)
        {
            return new(reason ?? loopExitReason, received, acknowledged, peakHandlers,
                operations.ToImmutableArray(), unacknowledged.ToImmutableArray(), failures.ToImmutableArray());
        }
    }

    public void Dispose()
    {
        cancellation.Dispose();
        Lifetime.Dispose();
    }
}

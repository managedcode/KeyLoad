using System.Runtime.ExceptionServices;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

/// <summary>Retains the original release failure; a repeated close cannot report a false successful join.</summary>
internal sealed class PartitionMovementRetainedWorkLease(NativeRequestWorkLease work) : IDisposable
{
    private readonly Lock gate = new();
    private bool attempted;
    private Exception? failure;

    public void Dispose()
    {
        lock (gate)
        {
            if (attempted)
            {
                if (failure is not null)
                { ExceptionDispatchInfo.Capture(failure).Throw(); }
                return;
            }
            attempted = true;
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(work.Dispose, failures);
            failure = failures.Count switch { PartitionMovementProtocol.NoFailures => null, PartitionMovementProtocol.SingleFailure => failures[PartitionMovementProtocol.FirstFailureIndex], _ => new AggregateException(failures) };
            if (failure is not null)
            { ExceptionDispatchInfo.Capture(failure).Throw(); }
        }
    }
}

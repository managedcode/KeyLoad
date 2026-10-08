using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns original capture allocation until a successful session/work transfer to the runtime registry.</summary>
internal static class PartitionMovementCaptureProducer
{
    private const int SingleFailure = 1;
    internal static (PartitionMovementCaptureHandle? Handle, Exception? Failure) Run(
        DatabaseEngine database, ICacheMemoryBudget memory, NativeRequestWorkOwner workOwner,
        IOptions<DatabaseLimits> limits, TimeProvider clock,
        PrincipalRecord principal, PartitionMovePeerEnvelope verified, PartitionMovementPendingCapture producer,
        Action<PartitionMovementCaptureHandle, PartitionMovementImageSession, NativeRequestWorkLease> publish,
        CancellationToken shutdown,
        CancellationToken cancellationToken)
    {
        PartitionMovementImageSession? session = null;
        NativeRequestWorkLease? retainedWork = null;
        PartitionMovementCaptureHandle? completed = null;
        Exception? failure = null;
        try
        {
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                retainedWork = workOwner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.ReadCapability);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(producer.StageCancellation, shutdown);
                ServerFailureObserver.Observe(() =>
                {
                    var work = new ReadExecutionBudget(limits, clock, cancellationToken);
                    using var stage = work.EnterStageCancellation(linked.Token);
                    ServerFailureObserver.Observe(() =>
                    {
                        var captured = PartitionMovementSourceCapture.Capture(database, memory, principal.Id, verified, work);
                        session = captured.Session;
                        work.CheckResult(captured.Handle);
                        publish(captured.Handle, captured.Session, retainedWork ?? throw new InvalidOperationException());
                        completed = captured.Handle;
                        session = null;
                        retainedWork = null;
                    }, failures);
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var failures = new List<Exception> { error };
            if (session is not null)
            { ServerFailureObserver.Observe(() => session.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
            if (retainedWork is not null)
            { ServerFailureObserver.Observe(retainedWork.Dispose, failures); }
            failure = failures.Count == SingleFailure ? error : new AggregateException(failures);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var failures = new List<Exception> { error };
            if (session is not null)
            { ServerFailureObserver.Observe(() => session.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
            if (retainedWork is not null)
            { ServerFailureObserver.Observe(retainedWork.Dispose, failures); }
            failure = failures.Count == SingleFailure ? error : new AggregateException(failures);
        }
        return (completed, failure);
    }
}

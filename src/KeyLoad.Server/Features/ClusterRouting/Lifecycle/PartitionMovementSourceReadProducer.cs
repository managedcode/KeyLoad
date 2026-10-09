using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Transfers one original reservation/work owner only after the registry accepts its completed image.</summary>
internal static class PartitionMovementSourceReadProducer
{
    internal static (T? Handle, Exception? Failure) Run<T>(NativeRequestWorkOwner workOwner,
        IOptions<DatabaseLimits> limits, TimeProvider clock, PartitionMovementPendingSourceRead<T> producer,
        Func<ReadExecutionBudget, (T Handle, PartitionMovementImageSession Session)> capture,
        Action<T, PartitionMovementImageSession, NativeRequestWorkLease> publish,
        CancellationToken shutdown, CancellationToken cancellationToken) where T : class
        => Run(workOwner, new ReadExecutionBudget(limits, clock, cancellationToken), producer, capture, publish,
            shutdown);

    internal static (T? Handle, Exception? Failure) Run<T>(NativeRequestWorkOwner workOwner,
        ReadExecutionBudget work, PartitionMovementPendingSourceRead<T> producer,
        Func<ReadExecutionBudget, (T Handle, PartitionMovementImageSession Session)> capture,
        Action<T, PartitionMovementImageSession, NativeRequestWorkLease> publish,
        CancellationToken shutdown) where T : class
    {
        PartitionMovementImageSession? session = null;
        NativeRequestWorkLease? retainedWork = null;
        T? completed = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            retainedWork = workOwner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.ReadCapability);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(producer.StageCancellation, shutdown);
            ServerFailureObserver.Observe(() =>
            {
                using var stage = work.EnterStageCancellation(linked.Token);
                ServerFailureObserver.Observe(() =>
                {
                    var captured = capture(work);
                    session = captured.Session;
                    work.CheckResult(captured.Handle);
                    publish(captured.Handle, captured.Session, retainedWork ?? throw new InvalidOperationException());
                    completed = captured.Handle;
                    session = null;
                    retainedWork = null;
                }, failures);
            }, failures);
        }, failures);
        if (session is not null)
        { ServerFailureObserver.Observe(() => session.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
        if (retainedWork is not null)
        { ServerFailureObserver.Observe(retainedWork.Dispose, failures); }
        return (completed, failures.Count switch { PartitionMovementProtocol.NoFailures => null, PartitionMovementProtocol.SingleFailure => failures[PartitionMovementProtocol.FirstFailureIndex], _ => new AggregateException(failures) });
    }
}

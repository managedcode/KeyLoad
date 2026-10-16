using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeLedgerNativeSession
{
    internal static QueryPage Read(SafeProjectorOperationDatabase owner, ReadExecutionBudget budget,
        NativeRequestWorkLease parent)
        => new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution()).ExecuteBound(P.Reader,
            ResourceLifetimeLedgerNativeData.Request(owner, P.Collection), budget,
            owner.Database.EvaluationClock, budget.Cancellation, parent);

    internal static async Task<ResourceLifetimeWorkResultRetention> RunAsync(SafeProjectorOperationDatabase owner,
        Func<NativeRequestWorkOwner, ResourceLifetimeWorkResultRetention, NativeRequestWorkLease,
            ReadExecutionBudget, DateTimeOffset, Task> body)
    {
        NativeRequestWorkOwner? workOwner = null;
        ResourceLifetimeWorkResultRetention? retention = null;
        NativeRequestWorkLease? parent = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            workOwner = new(UnitRoutingOptions.Routing());
            retention = new(owner.Database.OperationLimitsOptions);
            parent = workOwner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.RequestProducer);
            parent.AttachResultRetention(retention);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.CallerToken, parent.Cancellation);
            var budget = new ReadExecutionBudget(owner.Database.OperationLimitsOptions,
                owner.Database.EvaluationClock, linked.Token);
            var expiry = owner.Database.EvaluationClock.GetUtcNow().AddSeconds(owner.Database.Limits.QueryDeadlineSeconds);
            budget.ConstrainLifetime(expiry);
            await Assert.That(parent.ResultRetention).IsSameReferenceAs(retention);
            await body(workOwner, retention, parent, budget, expiry);
        }, failures);
        if (parent is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var original = failures.FirstOrDefault();
                var completion = parent.CompleteAsync(original, null);
                await Assert.That(completion).IsSameReferenceAs(parent.Settlement);
                var settled = await completion;
                await Assert.That(await parent.Settlement).IsSameReferenceAs(settled);
                if (original is null)
                {
                    await Assert.That(settled.OriginalFailure).IsNull();
                }
                else
                {
                    await Assert.That(settled.OriginalFailure).IsSameReferenceAs(original);
                }
                await Assert.That(settled.CleanupFailures.IsEmpty).IsTrue();
            }, failures);
        }
        if (workOwner is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => workOwner.DisposeAsync().AsTask(), failures);
        }
        if (retention is not null && (workOwner is null || workOwner.IsJoined))
        {
            ServerFailureObserver.Observe(retention.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return retention!;
    }
}

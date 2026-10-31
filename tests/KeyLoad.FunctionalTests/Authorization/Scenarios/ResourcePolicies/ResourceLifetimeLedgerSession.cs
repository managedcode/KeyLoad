using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Server;
using P = KeyLoad.FunctionalTests.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class ResourceLifetimeLedgerSession
{
    internal static QueryPage Read(SafeProjectorOperationDatabase owner, ReadExecutionBudget budget,
        RequestWorkLease parent)
        => new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution()).ExecuteBound(P.Reader,
            ResourceLifetimeLedgerData.Request(owner, P.Collection), budget,
            owner.Database.EvaluationClock, parent, budget.Cancellation);

    internal static async Task<ResourceLifetimeWorkResultRetention> RunAsync(SafeProjectorOperationDatabase owner,
        Func<RequestWorkOwner, ResourceLifetimeWorkResultRetention, RequestWorkLease,
            ReadExecutionBudget, Task> body)
    {
        RequestWorkOwner? workOwner = null;
        ResourceLifetimeWorkResultRetention? retention = null;
        RequestWorkLease? parent = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            workOwner = new();
            retention = new();
            parent = workOwner.Acquire(Guid.NewGuid(), RequestWorkKind.RequestProducer);
            parent.AttachResultRetention(retention);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.CallerToken, parent.Cancellation);
            var budget = new ReadExecutionBudget(owner.Database.EvaluationClock, linked.Token);
            await Assert.That(parent.ResultRetention).IsSameReferenceAs(retention);
            await body(workOwner, retention, parent, budget);
        }, failures);
        if (parent is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var original = failures.FirstOrDefault();
                var completion = parent.CompleteAsync(original, null);
                await Assert.That(ReferenceEquals(completion, parent.Settlement)).IsTrue();
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

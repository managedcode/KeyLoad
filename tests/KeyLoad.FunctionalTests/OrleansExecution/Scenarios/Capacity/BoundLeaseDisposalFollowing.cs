using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class BoundLeaseDisposalFollowing
{
    internal static async Task ExecuteAsync(BoundLeaseSeeded state,
        BoundLeaseOperation previous, CancellationToken token)
    {
        var database = state.Leader.Database.Database;
        using var ledger = new ResourceLifetimeWorkResultRetention();
        RequestWorkLease? parent = null;
        var errors = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            parent = previous.Owner.Acquire(previous.Parent.RequestId, RequestWorkKind.RequestProducer);
            parent.AttachResultRetention(ledger);
            var budget = new ReadExecutionBudget(database.EvaluationClock, token);
            budget.AttachResourceLifetimeBinding(parent);
            budget.ConstrainLifetime(previous.Expiry);
            var operation = new BoundLeaseOperation(previous.Owner, ledger, parent, budget,
                previous.Expiry, token);
            var input = BoundLeaseQuery.Create(state, operation);
            var result = BoundLeaseExecution.Read(state, operation, input);
            parent.AdmitPublication();
            await BoundLeaseAssertions.QueryAsync(state, input, result);
        }, errors);
        if (parent is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                parent.PrepareDisposal(errors.FirstOrDefault(), null);
                await parent.DisposeAsync();
                var outcome = await parent.Settlement;
                await Assert.That(outcome.OriginalFailure).IsSameReferenceAs(errors.FirstOrDefault());
                await Assert.That(outcome.CleanupFailures.IsEmpty).IsTrue();
            }, errors);
        }
        ServerFailureObserver.ThrowIfAny(errors);
    }
}

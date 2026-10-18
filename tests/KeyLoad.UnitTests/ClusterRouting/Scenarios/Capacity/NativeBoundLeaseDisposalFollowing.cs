using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalFollowing
{
    internal static async Task ExecuteAsync(NativeBoundLeaseSeeded state,
        NativeBoundLeaseOperation previous, CancellationToken token)
    {
        var database = state.Leader.Database.Database;
        using var ledger = new ResourceLifetimeWorkResultRetention(database.OperationLimitsOptions);
        NativeRequestWorkLease? parent = null;
        var errors = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            parent = previous.Owner.Acquire(previous.Parent.RequestId, NativeRequestWorkKind.RequestProducer);
            parent.AttachResultRetention(ledger);
            var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
            budget.AttachResourceLifetimeBinding(parent);
            budget.ConstrainLifetime(previous.Expiry);
            var operation = new NativeBoundLeaseOperation(previous.Owner, ledger, parent, budget,
                previous.Expiry, token);
            var input = NativeBoundLeaseQuery.Create(state, operation);
            var result = NativeBoundLeaseExecution.Read(state, operation, input);
            parent.AdmitPublication();
            await NativeBoundLeaseAssertions.QueryAsync(state, input, result);
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

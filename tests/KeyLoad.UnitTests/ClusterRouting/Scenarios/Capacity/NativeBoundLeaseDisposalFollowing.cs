using Microsoft.Extensions.DependencyInjection;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalFollowing
{
    internal static async Task ExecuteAsync(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeOperation previous, CancellationToken token)
    {
        var database = state.Leader.Database.Database;
        using var ledger = new ResourceLifetimeWorkResultRetention(database.OperationLimitsOptions);
        NativeRequestWorkLease? parent = null;
        NativeRequestWorkChildReservation? reservation = null;
        var errors = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            parent = previous.Owner.Acquire(previous.Parent.RequestId, NativeRequestWorkKind.RequestProducer);
            parent.AttachResultRetention(ledger);
            var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
            budget.AttachResourceLifetimeBinding(parent);
            budget.ConstrainLifetime(previous.Expiry);
            var operation = new NativeLocalChildLifetimeOperation(previous.Owner, ledger, parent, budget,
                previous.Codec, previous.Expiry, token);
            var input = NativeLocalChildLifetimePayload.Create(state, operation);
            reservation = parent.ReserveLocalChild(input.Verified, state.PhysicalOwner, state.Leader.Services.GetRequiredService<SiloStorageIdentityProvider>().Read());
            var child = NativeLocalChildLifetimeExecution.Consume(state, operation, input);
            var result = NativeLocalChildLifetimeExecution.Read(state, operation, input, child);
            child.AdmitPublication();
            await NativeLocalChildLifetimeAssertions.QueryAsync(state, input, result);
        }, errors);
        if (parent is not null)
        {
            await JoinAsync(reservation, parent, errors);
        }
        ServerFailureObserver.ThrowIfAny(errors);
    }

    private static async Task JoinAsync(NativeRequestWorkChildReservation? reservation,
        NativeRequestWorkLease parent, List<Exception> errors)
    {
        if (reservation is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reservation.Child.PrepareDisposal(errors.FirstOrDefault(), null);
                await reservation.Child.DisposeAsync();
                var report = await reservation.Child.Settlement;
                await Assert.That(report.CleanupFailures.IsEmpty).IsTrue();
            }, errors);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var report = await reservation.CompleteDispatchAsync(errors.FirstOrDefault(), []);
                await Assert.That(report.CleanupFailures.IsEmpty).IsTrue();
            }, errors);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            parent.PrepareDisposal(errors.FirstOrDefault(), null);
            await parent.DisposeAsync();
            var outcome = await parent.Settlement;
            await Assert.That(outcome.OriginalFailure).IsSameReferenceAs(errors.FirstOrDefault());
            await Assert.That(outcome.CleanupFailures.IsEmpty).IsTrue();
        }, errors);
    }
}

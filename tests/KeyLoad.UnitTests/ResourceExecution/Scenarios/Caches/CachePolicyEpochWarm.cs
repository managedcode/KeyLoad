using KeyLoad.Clustering;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ResourceExecution.CachePolicyEpochProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CachePolicyEpochWarm
{
    internal static async Task RunAsync(CachePolicyEpochState state, CancellationToken token)
    {
        await CachePolicyEpochProvision.SeedAsync(state, token);
        await CachePolicyEpochProvision.ResourceAsync(state, state.Resource with
        {
            SchemaVersion = 2L,
            FieldPolicies = [new(P.Path, P.Classification, P.Grant, P.UseGrant)]
        }, 1L, token);
        await CachePolicyEpochAssertions.ReadAsync(state, false, false, token);
        await CachePolicyEpochProvision.PrincipalAsync(state, state.Reader with
        {
            PolicyEpoch = 2L,
            FieldGrants = [P.Grant]
        }, token);
        await CachePolicyEpochAssertions.ReadAsync(state, true, false, token);
        await CachePolicyEpochProvision.PrincipalAsync(state, state.Reader with
        {
            PolicyEpoch = 3L,
            Grants = []
        }, token);
        await CachePolicyEpochRefusals.DeniedAsync(state, token);
        await CachePolicyEpochProvision.PrincipalAsync(state, state.Reader with
        {
            PolicyEpoch = 4L,
            Grants = [new(state.Partition.DatabaseId, P.Collection, Capability.DocumentsRead)]
        }, token);
        await CachePolicyEpochRefusals.CancelAsync(state);
        await CachePolicyEpochAssertions.ReadAsync(state, true, false, token);
        await PressureAsync(state, token);
        await CachePolicyEpochProvision.HealthyAsync(state, token);
        await CachePolicyEpochRefusals.ReplayAsync(state, token);
    }

    private static async Task PressureAsync(CachePolicyEpochState state, CancellationToken token)
    {
        var budget = state.Owner.Budget;
        var before = budget.GetSnapshot();
        var bytes = P.PoolBytes - before.RetainedBytes;
        await Assert.That(bytes).IsGreaterThan(0L);
        var failures = new List<Exception>();
        try
        {
            var accepted = budget.TryReserve(bytes, 0, out var reservation);
            using (reservation)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(accepted).IsTrue();
                    ArgumentNullException.ThrowIfNull(reservation);
                    await PressureReadAsync(state, reservation, bytes, token);
                }, failures);
            }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await CachePolicyEpochAssertions.BoundedAsync(state);
    }

    private static async Task PressureReadAsync(CachePolicyEpochState state, ICacheMemoryReservation reservation,
        long bytes, CancellationToken token)
    {
        var budget = state.Owner.Budget;
        var full = budget.GetSnapshot();
        await Assert.That(full.RetainedBytes).IsEqualTo(P.PoolBytes);
        var failures = new List<Exception>();
        try
        {
            var accepted = budget.TryReserve(1L, 0, out var refused);
            using (refused)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(accepted).IsFalse();
                    await Assert.That(refused).IsNull();
                    await CachePolicyEpochAssertions.EqualAsync(full, budget.GetSnapshot());
                    await CachePolicyEpochAssertions.ReadAsync(state, true, false, token);
                    await Assert.That(reservation.Bytes).IsEqualTo(bytes);
                    await Assert.That(reservation.Entries).IsEqualTo(0);
                }, failures);
            }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

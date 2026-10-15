using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PlacementCapacityAdmissionFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var original = new PlacementCapacityAdmissionOwner();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await original.ActiveAsync();
                await PlacementCapacityAdmissionQueue.FillAsync(original);
                await PlacementCapacityAdmissionQueue.CancelAndPromoteAsync(original);
                var shutdown = original.Admission.DisposeAsync().AsTask();
                original.Shutdown = shutdown;
                await Assert.That(ReferenceEquals(shutdown,
                    original.Admission.DisposeAsync().AsTask())).IsTrue();
                await Assert.That(shutdown.IsCompleted).IsFalse();
                await PlacementCapacityAdmissionQueue.JoinRefusedAsync(original);
                await PlacementCapacityAdmissionAssertions.ClosedAsync(original.Admission);
                await Assert.That(shutdown.IsCompleted).IsFalse();
                foreach (var lease in original.Leases)
                {
                    lease.Dispose();
                }
                await shutdown;
                await Assert.That(ReferenceEquals(shutdown,
                    original.Admission.DisposeAsync().AsTask())).IsTrue();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await HealthyAsync();
        await PlacementCapacityAdmissionFairness.RunAsync();
    }

    private static async Task HealthyAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var healthy = new PlacementCapacityAdmissionOwner();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await healthy.ActiveAsync();
                healthy.Queue();
                await Assert.That(healthy.Waiting.Single().IsCompleted).IsFalse();
                healthy.Leases.First().Dispose();
                var original = healthy.Waiting.Single();
                healthy.Leases.Add(await original);
                healthy.Observed.Add(original);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PlacementCapacityAdmissionFairness
{
    private const int Pending = 16;
    private const int First = 0;
    private const int Single = 1;

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new PlacementCapacityAdmissionOwner();
            await ServerFailureObserver.ObserveAsync(() => ProgressAsync(owner), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ProgressAsync(PlacementCapacityAdmissionOwner owner)
    {
        await owner.ActiveAsync();
        await PlacementCapacityAdmissionQueue.FillAsync(owner);
        var current = owner.Leases[First];
        for (var index = First; index < Pending; index++)
        {
            current = await PromoteAsync(owner, current, index);
            owner.Queue();
            await Assert.That(owner.Waiting[^Single].IsCompleted).IsFalse();
            await PlacementCapacityAdmissionAssertions.OverflowAsync(owner.Admission);
        }
        for (var index = Pending; index < Pending + Pending; index++)
        {
            current = await PromoteAsync(owner, current, index);
        }
        current.Dispose();
        owner.Leases[Single].Dispose();
        using var healthy = await owner.Admission.EnterAsync(CancellationToken.None);
    }

    private static async Task<PlacementIntentAdmissionLease> PromoteAsync(
        PlacementCapacityAdmissionOwner owner,
        PlacementIntentAdmissionLease current,
        int index)
    {
        current.Dispose();
        var original = owner.Waiting[index];
        var winner = await Task.WhenAny(owner.Waiting.Skip(index));
        await Assert.That(ReferenceEquals(winner, original)).IsTrue();
        var admitted = await original;
        owner.Leases.Add(admitted);
        owner.Observed.Add(original);
        await Assert.That(original.IsCompletedSuccessfully).IsTrue();
        foreach (var later in owner.Waiting.Skip(index + Single))
        {
            await Assert.That(later.IsCompleted).IsFalse();
        }
        return admitted;
    }
}

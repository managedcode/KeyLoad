namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PlacementCapacityAdmissionQueue
{
    private const int Pending = 16;
    private const int First = 0;
    private const int Next = 1;
    private const int SingleFailure = 1;

    internal static async Task FillAsync(PlacementCapacityAdmissionOwner owner)
    {
        for (var index = First; index < Pending; index++)
        {
            owner.Queue();
            await Assert.That(owner.Waiting[index].IsCompleted).IsFalse();
        }
        await PlacementCapacityAdmissionAssertions.OverflowAsync(owner.Admission);
    }

    internal static async Task CancelAndPromoteAsync(PlacementCapacityAdmissionOwner owner)
    {
        await owner.Callers[First].CancelAsync();
        var original = owner.Waiting[First];
        var failures = new List<Exception>();
        await owner.JoinAsync(original, failures);
        await Assert.That(failures.Count).IsEqualTo(SingleFailure);
        await Assert.That(failures[First]).IsTypeOf<TaskCanceledException>();
        var cancellation = (OperationCanceledException)failures[First];
        await Assert.That(cancellation.CancellationToken).IsEqualTo(owner.Callers[First].Token);
        owner.Queue();
        await Assert.That(owner.Waiting[^1].IsCompleted).IsFalse();
        await PlacementCapacityAdmissionAssertions.OverflowAsync(owner.Admission);
        owner.Leases[First].Dispose();
        var promoted = owner.Waiting[Next];
        owner.Leases.Add(await promoted);
        owner.Observed.Add(promoted);
        foreach (var queued in owner.Waiting.Skip(Next + Next))
        { await Assert.That(queued.IsCompleted).IsFalse(); }
    }

    internal static async Task JoinRefusedAsync(PlacementCapacityAdmissionOwner owner)
    {
        foreach (var original in owner.Waiting.Where(item => !owner.Observed.Contains(item)))
        {
            var failures = new List<Exception>();
            await owner.JoinAsync(original, failures);
            await Assert.That(failures.Count).IsEqualTo(SingleFailure);
            await Assert.That(failures[First]).IsTypeOf<KeyLoadException>();
            await PlacementCapacityAdmissionAssertions.RequireBoundAsync((KeyLoadException)failures[First]);
        }
    }
}

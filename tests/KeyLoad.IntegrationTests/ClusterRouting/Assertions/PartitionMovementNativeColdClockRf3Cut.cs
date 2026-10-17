using KeyLoad.Core;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Accounts only genuine operation clocks; native silo membership has no canonical database CAS rows.</summary>
internal sealed class PartitionMovementNativeColdClockRf3Cut
{
    private readonly PartitionMovementPublicParentRf3NativeCut after;
    private readonly HashSet<string> allowed;
    internal DateTimeOffset ExpectedClock
    {
        get; private set;
    }

    internal PartitionMovementNativeColdClockRf3Cut(PartitionMovementPublicParentRf3NativeCut before,
        PartitionMovementPublicParentRf3NativeCut after, HashSet<string> allowed)
    {
        this.after = after;
        this.allowed = allowed;
        ExpectedClock = PartitionMovementCapturePointerRf3Fault.Read<DateTimeOffset>(before, KeySpace.ClockBytes);
    }

    internal void ObserveClock(ReplicatedOperation operation)
    {
        if (operation.EvaluatedAt > ExpectedClock)
        {
            ExpectedClock = operation.EvaluatedAt;
        }
        allowed.Add(Convert.ToHexString(KeySpace.ClockBytes));
    }

    internal async Task RequireCompleteAsync()
    {
        await Assert.That(PartitionMovementCapturePointerRf3Fault.Read<DateTimeOffset>(after, KeySpace.ClockBytes))
            .IsEqualTo(ExpectedClock);
    }
}

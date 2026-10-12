using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeNativeLifetimeTests
{
    [Test]
    public async Task AdmissionIsBoundedAndStopJoinsTheOriginalLeaseAfterClosingAndCancelling()
    {
        await using var owner = Create(enabled: true);
        using var first = owner.Acquire(Guid.NewGuid());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => owner.Acquire(Guid.NewGuid())).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
        var cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observed = first.ShutdownToken.Register(() => cancellation.TrySetResult());
        var stopped = owner.StopAsync();
        await Assert.That(stopped.IsCompleted).IsFalse();
        await cancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(first.ShutdownToken.IsCancellationRequested).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => owner.Acquire(Guid.NewGuid())).Code)
            .IsEqualTo(ErrorCode.OwnershipLost);
        first.Dispose();
        first.Dispose();
        await stopped;
        await owner.StopAsync();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => owner.Acquire(Guid.NewGuid())).Code)
            .IsEqualTo(ErrorCode.OwnershipLost);
    }

    [Test]
    public async Task ReturnedLeaseRestoresExactlyOnePermitAndDisabledOwnerNeverAdmits()
    {
        await using var owner = Create(enabled: true);
        var first = owner.Acquire(Guid.NewGuid());
        first.Dispose();
        first.Dispose();
        using var next = owner.Acquire(Guid.NewGuid());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => owner.Acquire(Guid.NewGuid())).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
        await using var disabled = Create(enabled: false);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => disabled.Acquire(Guid.NewGuid())).Code)
            .IsEqualTo(ErrorCode.UnsupportedCapability);
        await disabled.StopAsync();
    }

    private static PhysicalOwnerProbeWorkOwner Create(bool enabled)
        => new(Options.Create(new GrainRoutingOptions()), Options.Create(new NodeOptions
        { PhysicalGroupTrust = new() { RegisterPhysicalOwners = enabled } }),
            Options.Create(new PhysicalOwnerExecutionOptions { MaximumAdmissions = 1 }));
}

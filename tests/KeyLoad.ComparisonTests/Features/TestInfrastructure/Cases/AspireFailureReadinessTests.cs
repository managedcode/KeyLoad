using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureReadinessTests
{
    [Test]
    [Arguments(nameof(KnownResourceStates.FailedToStart))]
    [Arguments(nameof(KnownResourceStates.RuntimeUnhealthy))]
    [Arguments(nameof(KnownResourceStates.Exited))]
    public async Task AcTest014NativeReadinessFailureCancelsAndJoinsStartingSibling(string state)
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        await fixture.PublishAsync(fixture.Leaf, KnownResourceStates.Starting).WaitAsync(deadline.Token);
        var readiness = AspireStartupReadiness.WaitForHealthyAsync(fixture.Application,
            [fixture.Server.Name, fixture.Leaf.Name], deadline.Token);
        try
        {
            await AspireFailureAssertions.SettlesFailedAsync<DistributedApplicationException>(readiness,
                () => fixture.PublishAsync(fixture.Server, state), deadline.Token);
            await Assert.That(fixture.Notifications.TryGetCurrentState(fixture.Leaf.Name, out var sibling)).IsTrue();
            await Assert.That(sibling!.Snapshot.State!.Text).IsEqualTo(KnownResourceStates.Starting);
            await fixture.PublishAsync(fixture.Leaf, KnownResourceStates.Exited, 137).WaitAsync(deadline.Token);
            await Assert.ThrowsAsync<DistributedApplicationException>(() => readiness);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, readiness);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcTest014ReadinessCallerCancellationAndHostStopSettleEveryNativeWait(bool hostStopping)
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var readiness = AspireStartupReadiness.WaitForHealthyAsync(fixture.Application,
            [fixture.Server.Name, fixture.Leaf.Name], caller.Token);
        try
        {
            await AspireFailureAssertions.SettlesFailedAsync<OperationCanceledException>(readiness, async () =>
            {
                if (hostStopping)
                {
                    fixture.StopHost();
                }
                else
                {
                    await caller.CancelAsync();
                }
            }, deadline.Token);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(caller, readiness);
        }
    }
}

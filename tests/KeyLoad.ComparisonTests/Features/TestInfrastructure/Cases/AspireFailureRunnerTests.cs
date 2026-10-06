using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureRunnerTests
{
    [Test]
    [Arguments(nameof(KnownResourceStates.FailedToStart))]
    [Arguments(nameof(KnownResourceStates.RuntimeUnhealthy))]
    [Arguments(nameof(KnownResourceStates.Finished))]
    [Arguments(nameof(KnownResourceStates.Exited))]
    public async Task AcTest012RunnerTerminalWithoutOriginalExitFailsWithinFiveSeconds(string state)
    {
        await using var fixture = new AspireFailureNotificationFixture(includeDependencies: false);
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await AspireFailureAssertions.SettlesFailedAsync<DistributedApplicationException>(completion,
                () => fixture.PublishAsync(fixture.Runner, state), deadline.Token);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(17)]
    public async Task AcTest014NoDependencyRunnerRetainsItsOriginalNativeExit(int exit)
    {
        await using var fixture = new AspireFailureNotificationFixture(includeDependencies: false);
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await fixture.PublishAsync(fixture.Runner, KnownResourceStates.Exited, exit).WaitAsync(deadline.Token);
            await Assert.That(await completion.WaitAsync(AspireFailureAssertions.EventDeadline, TimeProvider.System, deadline.Token)).IsEqualTo(exit);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    [Arguments(nameof(KnownResourceStates.FailedToStart))]
    [Arguments(nameof(KnownResourceStates.RuntimeUnhealthy))]
    public async Task AcTest012FailedRunnerCannotSucceedWithZeroExit(string state)
    {
        await using var fixture = new AspireFailureNotificationFixture(includeDependencies: false);
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await AspireFailureAssertions.SettlesFailedAsync<DistributedApplicationException>(completion,
                () => fixture.PublishAsync(fixture.Runner, state, 0), deadline.Token);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    public async Task AcTest012AlreadyFailedDependencyPreventsOwnedApplicationStartup()
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        await fixture.PublishAsync(fixture.Leaf, KnownResourceStates.FailedToStart).WaitAsync(deadline.Token);
        var execution = AspireResourceCompletion.RunToExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await Assert.ThrowsAsync<DistributedApplicationException>(() => execution.WaitAsync(AspireFailureAssertions.EventDeadline, TimeProvider.System, deadline.Token));
            await Assert.That(fixture.Notifications.TryGetCurrentState(fixture.Runner.Name, out _)).IsFalse();
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, execution);
        }
    }
}

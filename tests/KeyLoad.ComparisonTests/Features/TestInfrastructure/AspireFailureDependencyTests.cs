using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureDependencyTests
{
    [Test]
    [Arguments(KnownResourceStates.FailedToStart, null)]
    [Arguments(KnownResourceStates.RuntimeUnhealthy, null)]
    [Arguments(KnownResourceStates.Finished, 0)]
    [Arguments(KnownResourceStates.Exited, 0)]
    [Arguments(KnownResourceStates.Running, 7)]
    public async Task AcTest012TransitiveLongLivedDependencyTerminalDuringLoadFailsPromptly(string state, int? exit)
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await fixture.PublishAsync(fixture.Runner, KnownResourceStates.Running).WaitAsync(deadline.Token);
            await AspireFailureAssertions.SettlesFailedAsync<DistributedApplicationException>(completion,
                () => fixture.PublishAsync(fixture.Leaf, state, exit), deadline.Token);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    [Arguments(KnownResourceStates.Finished, null)]
    [Arguments(KnownResourceStates.Finished, 3)]
    [Arguments(KnownResourceStates.FailedToStart, 0)]
    [Arguments(KnownResourceStates.RuntimeUnhealthy, 0)]
    public async Task AcTest013BootstrapMustHaveItsExpectedOriginalExitAndCannotFail(string state, int? exit)
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await AspireFailureAssertions.SettlesFailedAsync<DistributedApplicationException>(completion,
                () => fixture.PublishAsync(fixture.Bootstrap, state, exit), deadline.Token);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(11)]
    public async Task AcTest013SuccessfulBootstrapDoesNotCompleteTheRunnerGuard(int expectedExit)
    {
        await using var fixture = new AspireFailureNotificationFixture(bootstrapExit: expectedExit);
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await fixture.PublishAsync(fixture.Bootstrap, KnownResourceStates.Finished, expectedExit).WaitAsync(deadline.Token);
            await Assert.That(completion.IsCompleted).IsFalse();
            await fixture.PublishAsync(fixture.Runner, KnownResourceStates.Exited, 17).WaitAsync(deadline.Token);
            await Assert.That(await completion.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token)).IsEqualTo(17);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }
}

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureDependencyTests
{
    [Test]
    [Arguments(nameof(KnownResourceStates.FailedToStart), null)]
    [Arguments(nameof(KnownResourceStates.RuntimeUnhealthy), null)]
    [Arguments(nameof(KnownResourceStates.Finished), 0)]
    [Arguments(nameof(KnownResourceStates.Exited), 0)]
    [Arguments(nameof(KnownResourceStates.Running), 7)]
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
    [Arguments(nameof(KnownResourceStates.Finished), null)]
    [Arguments(nameof(KnownResourceStates.Finished), 3)]
    [Arguments(nameof(KnownResourceStates.FailedToStart), 0)]
    [Arguments(nameof(KnownResourceStates.RuntimeUnhealthy), 0)]
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
            await Assert.That(await completion.WaitAsync(AspireFailureAssertions.EventDeadline, TimeProvider.System, deadline.Token)).IsEqualTo(17);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }
}

using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureCancellationTests
{
    [Test]
    public async Task AcTest013TransientHealthAndNonterminalStatesPreserveNativeRunnerExit()
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await fixture.PublishAsync(fixture.Server, KnownResourceStates.Starting).WaitAsync(deadline.Token);
            await fixture.PublishAsync(fixture.Server, KnownResourceStates.Running, health: HealthStatus.Unhealthy).WaitAsync(deadline.Token);
            await fixture.PublishAsync(fixture.Leaf, KnownResourceStates.Waiting).WaitAsync(deadline.Token);
            await fixture.PublishAsync(fixture.Bootstrap, KnownResourceStates.Finished, 0).WaitAsync(deadline.Token);
            await fixture.PublishAsync(fixture.Runner, KnownResourceStates.Running, health: HealthStatus.Unhealthy).WaitAsync(deadline.Token);
            await Assert.That(completion.IsCompleted).IsFalse();
            await fixture.PublishAsync(fixture.Runner, KnownResourceStates.Exited, 0).WaitAsync(deadline.Token);
            await Assert.That(await completion.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token)).IsEqualTo(0);
            await fixture.PublishAsync(fixture.Leaf, KnownResourceStates.Exited, 137).WaitAsync(deadline.Token);
            await Assert.That(await completion).IsEqualTo(0);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcTest014CallerCancellationAndHostStoppingTerminateAndJoinNativeObservation(bool hostStopping)
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, caller.Token);
        try
        {
            await AspireFailureAssertions.SettlesFailedAsync<OperationCanceledException>(completion, async () =>
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
            await AspireFailureAssertions.JoinAsync(caller, completion);
        }
    }
}

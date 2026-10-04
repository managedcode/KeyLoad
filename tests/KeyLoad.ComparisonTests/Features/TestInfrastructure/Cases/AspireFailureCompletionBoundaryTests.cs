using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureCompletionBoundaryTests
{
    [Test]
    public async Task AcTest013PendingBootstrapCannotAuthorizeSuccessfulRunnerExit()
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await AspireFailureAssertions.SettlesFailedAsync<DistributedApplicationException>(completion,
                () => fixture.PublishAsync(fixture.Runner, KnownResourceStates.Exited, 0), deadline.Token);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    public async Task AcTest013CachedSuccessfulBootstrapAndRunnerRetainOriginalExit()
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        await fixture.PublishAsync(fixture.Bootstrap, KnownResourceStates.Finished, 0).WaitAsync(deadline.Token);
        await fixture.PublishAsync(fixture.Runner, KnownResourceStates.Exited, 0).WaitAsync(deadline.Token);
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
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
    public async Task AcTest014NativeFailureDiagnosticNamesOnlyTheOwnedResourceAndClosedState()
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await fixture.PublishAsync(fixture.Leaf, KnownResourceStates.Exited, 137).WaitAsync(deadline.Token);
            var error = await Assert.ThrowsAsync<DistributedApplicationException>(() => completion.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token));
            await Assert.That(error).IsNotNull();
            await Assert.That(error!.Message).Contains(fixture.Leaf.Name);
            await Assert.That(error.Message).Contains(KnownResourceStates.Exited);
            await Assert.That(error.Message).Contains("137");
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    public async Task AcTest013QueuedPostRunnerFaultDoesNotRetroactivelyInvalidateNativeExit()
    {
        await using var fixture = new AspireFailureNotificationFixture();
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await fixture.PublishAsync(fixture.Bootstrap, KnownResourceStates.Finished, 0).WaitAsync(deadline.Token);
            var runnerPublication = fixture.PublishAsync(fixture.Runner, KnownResourceStates.Exited, 0);
            var laterFault = fixture.PublishAsync(fixture.Leaf, KnownResourceStates.Exited, 137);
            await Task.WhenAll(runnerPublication, laterFault).WaitAsync(deadline.Token);
            await Assert.That(await completion.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token)).IsEqualTo(0);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }

    [Test]
    public async Task AcTest013WithoutLifetimeConfigurationIsNotAnOwnedExecutionDependency()
    {
        await using var fixture = new AspireFailureNotificationFixture(includeDependencies: false);
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var completion = AspireResourceCompletion.WaitForExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await fixture.PublishAsync(fixture.Configuration, KnownResourceStates.RuntimeUnhealthy).WaitAsync(deadline.Token);
            await fixture.PublishAsync(fixture.Runner, KnownResourceStates.Exited, 0).WaitAsync(deadline.Token);
            await Assert.That(await completion.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token)).IsEqualTo(0);
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, completion);
        }
    }
}

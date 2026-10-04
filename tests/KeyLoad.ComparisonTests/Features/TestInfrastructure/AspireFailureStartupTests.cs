using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureStartupTests
{
    [Test]
    public async Task AcTest014RequiredFailureDuringNativeBeforeStartCancelsAndJoinsStartup()
    {
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task BeforeStart(BeforeStartEvent update, CancellationToken token)
        {
            entered.SetResult();
            try
            {
                var dependency = update.Model.Resources.Single(resource => resource.Name == AspireFailureNotificationFixture.LeafName);
                await update.Services.GetRequiredService<ResourceNotificationService>().PublishUpdateAsync(dependency,
                    snapshot => snapshot with { State = KnownResourceStates.FailedToStart });
                await blocked.Task.WaitAsync(token);
            }
            finally
            {
                settled.SetResult();
            }
        }
        await using var fixture = new AspireFailureNotificationFixture(beforeStart: BeforeStart);
        var execution = AspireResourceCompletion.RunToExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            await Assert.ThrowsAsync<DistributedApplicationException>(() => execution.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token));
            await Assert.That(settled.Task.IsCompletedSuccessfully).IsTrue();
            await Assert.That(execution.IsCompleted).IsTrue();
        }
        finally
        {
            blocked.TrySetResult();
            await AspireFailureAssertions.JoinAsync(deadline, execution);
            await settled.Task.WaitAsync(deadline.Token);
        }
    }

    [Test]
    public async Task AcTest014ActualNativeBeforeStartFileFailureRetainsOriginalExceptionAndJoinsObserver()
    {
        var directory = Directory.CreateTempSubdirectory("keyload-aspire-start-failure-");
        using var deadline = AspireFailureAssertions.CreateDeadline();
        async Task BeforeStart(BeforeStartEvent update, CancellationToken token)
        {
            _ = update.Services.GetRequiredService<ResourceNotificationService>();
            await File.ReadAllTextAsync(Path.Combine(directory.FullName, "missing-startup-input"), token);
        }
        var fixture = new AspireFailureNotificationFixture(beforeStart: BeforeStart);
        var execution = AspireResourceCompletion.RunToExitAsync(fixture.Application, fixture.Runner.Name, deadline.Token);
        try
        {
            var error = await Assert.ThrowsAsync<FileNotFoundException>(() => execution.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token));
            await Assert.That(error.FileName).IsEqualTo(Path.Combine(directory.FullName, "missing-startup-input"));
            await Assert.That(execution.IsCompleted).IsTrue();
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, execution);
            try
            {
                await fixture.DisposeAsync();
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        }
    }
}

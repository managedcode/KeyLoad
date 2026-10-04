using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureNativeProcessTests
{
    [Test]
    public async Task AcTest012ActualOwnedMissingExecutableFailsAndPreservesNativeOutcome()
    {
        var directory = Directory.CreateTempSubdirectory("keyload-aspire-process-failure-");
        using var deadline = AspireFailureAssertions.CreateDeadline();
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true });
        var runner = builder.AddExecutable(AspireFailureNotificationFixture.RunnerName,
            Path.Combine(directory.FullName, "missing-owned-executable"), directory.FullName);
        var application = builder.Build();
        var failure = application.ResourceNotifications.WaitForResourceAsync(runner.Resource.Name,
            update => AspireTerminalResource.IsTerminal(update.Snapshot), deadline.Token);
        var execution = AspireResourceCompletion.RunToExitAsync(application, runner.Resource.Name, deadline.Token);
        try
        {
            var native = await failure.WaitAsync(deadline.Token);
            if (native.Snapshot.ExitCode is { } exit && native.Snapshot.State?.Text is not
                (KnownResourceStates.FailedToStart or KnownResourceStates.RuntimeUnhealthy))
            {
                await Assert.That(exit).IsNotEqualTo(0);
                await Assert.That(await execution.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token)).IsEqualTo(exit);
            }
            else
            {
                await Assert.ThrowsAsync<DistributedApplicationException>(() => execution.WaitAsync(AspireFailureAssertions.EventDeadline, deadline.Token));
            }
        }
        finally
        {
            await AspireFailureAssertions.JoinAsync(deadline, failure, execution);
            try
            {
                using var cleanup = AspireFailureAssertions.CreateDeadline();
                await application.StopAsync(cleanup.Token);
            }
            finally
            {
                try
                {
                    await application.DisposeAsync();
                }
                finally
                {
                    directory.Delete(recursive: true);
                }
            }
        }
    }
}

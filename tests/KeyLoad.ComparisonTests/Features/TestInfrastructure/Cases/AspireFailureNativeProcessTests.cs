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
        try
        {
            await VerifyAndJoinFailureAsync(application, runner.Resource.Name, deadline);
        }
        finally
        {
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

    private static async Task VerifyAndJoinFailureAsync(DistributedApplication application, string runnerName, CancellationTokenSource deadline)
    {
        var failure = WaitForFailureAsync(application, runnerName, deadline.Token);
        var execution = AspireResourceCompletion.RunToExitAsync(application, runnerName, deadline.Token);
        try
        {
            var native = await failure.WaitAsync(deadline.Token);
            if (native.Snapshot.ExitCode is { } exit && !AspireTerminalResource.HasFailedState(native.Snapshot))
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
            await deadline.CancelAsync();
            await ((Task)failure).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await ((Task)execution).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static async Task<ResourceEvent> WaitForFailureAsync(DistributedApplication application, string resourceName, CancellationToken token)
        => await application.ResourceNotifications.WaitForResourceAsync(resourceName,
            update => AspireTerminalResource.IsTerminal(update.Snapshot), token).ConfigureAwait(false);
}

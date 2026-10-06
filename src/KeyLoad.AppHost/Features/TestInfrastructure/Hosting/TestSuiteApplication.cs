using System.Runtime.ExceptionServices;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteApplication
{
    internal static async Task<int> RunAsync(DistributedApplication app, TestSuiteSettings settings)
    {
        const int SingleFailureCount = 1;
        const int IndexValue = 0;
        const int BoundaryValue = 1;
        const string MessageText = "Aspire test execution or cleanup failed.";

        var policy = app.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value;
        var nativeCoverageCleanup = settings.NativeCoverageRf3 is null
            ? null : app.Services.GetRequiredService<NativeCoverageRf3Cleanup>();
        var failures = new List<Exception>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
        deadline.CancelAfter(settings.Timeout);
        using var outputLifetime = new CancellationTokenSource();
        var output = ForwardOutputAsync(app, settings, outputLifetime.Token);
        var execution = ExecuteAsync(app, settings.ResourceName, deadline.Token);
        try
        {
            await CollectAsync(() => execution, failures).ConfigureAwait(false);
        }
        finally
        {
            if (settings.NativeCoverageRf3 is null)
            {
                await CleanupOrdinaryAsync(app, settings, policy, nativeCoverageCleanup, outputLifetime,
                    output, failures).ConfigureAwait(false);
            }
            else
            {
                await CleanupNativeCoverageAsync(app, policy, nativeCoverageCleanup!, outputLifetime,
                    output, failures).ConfigureAwait(false);
            }
        }
        if (failures.Count == SingleFailureCount)
        {
            ExceptionDispatchInfo.Capture(failures[IndexValue]).Throw();
        }
        if (failures.Count > BoundaryValue)
        {
            throw new AggregateException(MessageText, failures);
        }
        return await execution.ConfigureAwait(false);
    }

    private static async Task CleanupOrdinaryAsync(DistributedApplication app, TestSuiteSettings settings,
        TestExecutionOptions policy, NativeCoverageRf3Cleanup? nativeCoverageCleanup,
        CancellationTokenSource outputLifetime, Task output, List<Exception> failures)
    {
        using var cleanup = new CancellationTokenSource(policy.ApplicationCleanupTimeout);
        await CollectAsync(() => app.StopAsync(cleanup.Token), failures).ConfigureAwait(false);
        if (settings.LocalRf3ImageEnabled)
        {
            using var imageCleanup = new CancellationTokenSource(policy.ImageCleanupTimeout);
            await CollectAsync(() => app.Services.GetRequiredService<LocalRf3ImageCleanup>()
                .CleanupAsync(imageCleanup.Token), failures).ConfigureAwait(false);
        }
        await CollectAsync(outputLifetime.CancelAsync, failures).ConfigureAwait(false);
        await CollectAsync(() => output, failures).ConfigureAwait(false);
        await CollectAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (nativeCoverageCleanup is not null)
        {
            using var imageCleanup = new CancellationTokenSource(policy.ImageCleanupTimeout);
            await CollectAsync(() => nativeCoverageCleanup.CleanupAsync(imageCleanup.Token), failures)
                .ConfigureAwait(false);
        }
    }

    private static async Task CleanupNativeCoverageAsync(DistributedApplication app, TestExecutionOptions policy,
        NativeCoverageRf3Cleanup nativeCoverageCleanup, CancellationTokenSource outputLifetime, Task output,
        List<Exception> failures)
    {
        using var deadline = new NativeCoverageCleanupDeadline(policy.ApplicationCleanupTimeout);
        await deadline.CollectAsync(() => app.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await deadline.CollectAsync(outputLifetime.CancelAsync, failures).ConfigureAwait(false);
        await deadline.CollectAsync(() => output, failures).ConfigureAwait(false);
        await deadline.CollectAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        using var imageCleanup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        imageCleanup.CancelAfter(policy.ImageCleanupTimeout);
        await deadline.CollectAsync(() => nativeCoverageCleanup.CleanupAsync(imageCleanup.Token), failures)
            .ConfigureAwait(false);
    }

    private static async Task<int> ExecuteAsync(DistributedApplication app, string resource, CancellationToken token)
        => await AspireResourceCompletion.RunToExitAsync(app, resource, token).ConfigureAwait(false);

    private static Task ForwardOutputAsync(DistributedApplication app, TestSuiteSettings settings, CancellationToken token)
    {
        string[] resources = settings.Suite == PriorProbeResources.RecoverySuite
            ? [settings.ResourceName, PriorProbeResources.Native5Resource, PriorProbeResources.Native6Resource]
            : [settings.ResourceName];
        if (settings.LocalRf3ImageEnabled)
        {
            resources = [.. resources, LocalRf3ImagePrerequisite.ResourceName];
        }
        if (settings.NativeCoverageRf3 is not null)
        {
            resources = [.. resources, NativeCoverageRf3Prerequisite.ResourceName];
        }
        return Task.WhenAll(resources.Select(name => TestSuiteOutput.ForwardAsync(app, name, token)));
    }

    private static async Task CollectAsync(Func<Task> action, List<Exception> failures)
    {
        var pending = InvokeAsync(action);
        await pending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (pending.IsFaulted)
        {
            failures.AddRange(pending.Exception!.InnerExceptions);
        }
        if (pending.IsCanceled)
        {
            try
            { await pending.ConfigureAwait(false); }
            catch (OperationCanceledException exception) { failures.Add(exception); }
        }
    }

    private static async Task InvokeAsync(Func<Task> action)
        => await action().ConfigureAwait(false);
}

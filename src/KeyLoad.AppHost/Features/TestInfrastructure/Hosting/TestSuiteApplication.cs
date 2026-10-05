using System.Runtime.ExceptionServices;
using KeyLoad.AppHost.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteApplication
{
    internal static async Task<int> RunAsync(DistributedApplication app, TestSuiteSettings settings)
    {
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
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await CollectAsync(() => app.StopAsync(cleanup.Token), failures).ConfigureAwait(false);
            if (settings.LocalRf3ImageEnabled)
            {
                using var imageCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await CollectAsync(() => app.Services.GetRequiredService<LocalRf3ImageCleanup>()
                    .CleanupAsync(imageCleanup.Token), failures).ConfigureAwait(false);
            }
            await CollectAsync(outputLifetime.CancelAsync, failures).ConfigureAwait(false);
            await CollectAsync(() => output, failures).ConfigureAwait(false);
            await CollectAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException("Aspire test execution or cleanup failed.", failures);
        }
        return await execution.ConfigureAwait(false);
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

using System.Runtime.ExceptionServices;
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
        var output = TestSuiteOutput.ForwardAsync(app, settings.ResourceName, outputLifetime.Token);
        var execution = ExecuteAsync(app, settings.ResourceName, deadline.Token);
        try
        {
            await CollectAsync(() => execution, failures).ConfigureAwait(false);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await CollectAsync(() => app.StopAsync(cleanup.Token), failures).ConfigureAwait(false);
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
    {
        await app.StartAsync(token).ConfigureAwait(false);
        return await WaitForExitAsync(app, resource, token).ConfigureAwait(false);
    }

    private static async Task<int> WaitForExitAsync(DistributedApplication app, string resource, CancellationToken token)
    {
        await app.ResourceNotifications.WaitForResourceAsync(resource,
            state => state.Snapshot.ExitCode is not null || state.Snapshot.State?.Text == KnownResourceStates.FailedToStart,
            token).ConfigureAwait(false);
        if (!app.ResourceNotifications.TryGetCurrentState(resource, out var state) || state.Snapshot.ExitCode is null)
        {
            throw new InvalidOperationException("The Aspire test runner failed to start or has no original exit status.");
        }
        return state.Snapshot.ExitCode.Value;
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
            try { await pending.ConfigureAwait(false); }
            catch (OperationCanceledException exception) { failures.Add(exception); }
        }
    }

    private static async Task InvokeAsync(Func<Task> action)
        => await action().ConfigureAwait(false);
}

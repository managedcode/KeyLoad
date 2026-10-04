using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class AspireResourceCompletion
{
    internal static async Task<int> RunToExitAsync(DistributedApplication app, string runnerName, CancellationToken token)
    {
        using var lifetime = CreateLifetime(app, token);
        var completion = WaitForExitAsync(app, runnerName, lifetime.Token);
        if (completion.IsCompleted)
        {
            return await completion.ConfigureAwait(false);
        }
        var startup = StartAsync(app, lifetime.Token);
        Task[] owned = [completion, startup];
        var primary = await Task.WhenAny(owned).ConfigureAwait(false);
        if (ReferenceEquals(primary, startup) && startup.IsCompletedSuccessfully)
        {
            primary = completion;
        }
        await AspireOwnedTaskJoin.CompleteAsync(lifetime, owned, primary).ConfigureAwait(false);
        return await completion.ConfigureAwait(false);
    }

    internal static async Task<int> WaitForExitAsync(DistributedApplication app, string runnerName, CancellationToken token)
    {
        var dependencies = AspireRequiredResources.ForRunner(app, runnerName);
        var pending = dependencies.Where(item => item.Value is not null).Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var lifetime = CreateLifetime(app, token);
        lifetime.Token.ThrowIfCancellationRequested();
        ValidateCurrentDependencies(app, dependencies, pending);
        if (app.ResourceNotifications.TryGetCurrentState(runnerName, out var current)
            && AspireTerminalResource.IsTerminal(current.Snapshot))
        {
            return ResolveRunnerExit(runnerName, current.Snapshot, pending);
        }
        await foreach (var update in app.ResourceNotifications.WatchAsync(lifetime.Token).ConfigureAwait(false))
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (dependencies.TryGetValue(update.Resource.Name, out var expectedExit))
            {
                AspireTerminalResource.ValidateDependency(update.Resource.Name, update.Snapshot, expectedExit);
                if (expectedExit is not null && AspireTerminalResource.IsTerminal(update.Snapshot))
                {
                    pending.Remove(update.Resource.Name);
                }
            }
            if (string.Equals(update.Resource.Name, runnerName, StringComparison.OrdinalIgnoreCase)
                && AspireTerminalResource.IsTerminal(update.Snapshot))
            {
                return ResolveRunnerExit(update.Resource.Name, update.Snapshot, pending);
            }
        }
        lifetime.Token.ThrowIfCancellationRequested();
        throw new OperationCanceledException(lifetime.Token);
    }

    private static CancellationTokenSource CreateLifetime(DistributedApplication app, CancellationToken token)
        => CancellationTokenSource.CreateLinkedTokenSource(token,
            app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);

    private static int ResolveRunnerExit(string resourceName, CustomResourceSnapshot snapshot, HashSet<string> pending)
    {
        var exit = AspireTerminalResource.RunnerExit(resourceName, snapshot);
        if (exit == 0)
        {
            AspireTerminalResource.RequireCompletions(pending);
        }
        return exit;
    }

    private static void ValidateCurrentDependencies(DistributedApplication app, Dictionary<string, int?> dependencies, HashSet<string> pending)
    {
        foreach (var (resource, expectedExit) in dependencies)
        {
            if (app.ResourceNotifications.TryGetCurrentState(resource, out var current))
            {
                AspireTerminalResource.ValidateDependency(resource, current.Snapshot, expectedExit);
                if (expectedExit is not null && AspireTerminalResource.IsTerminal(current.Snapshot))
                {
                    pending.Remove(resource);
                }
            }
        }
    }

    private static async Task StartAsync(DistributedApplication app, CancellationToken token)
        => await app.StartAsync(token).ConfigureAwait(false);
}

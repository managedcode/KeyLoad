using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class AspireStartupReadiness
{
    internal static async Task WaitForHealthyAsync(DistributedApplication app, IEnumerable<string> resourceNames, CancellationToken token)
    {
        var names = resourceNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token,
            app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
        lifetime.Token.ThrowIfCancellationRequested();
        var waits = names.Select(name => WaitForNativeHealthyAsync(app, name, lifetime.Token)).ToArray();
        var primary = await AspireOwnedTaskJoin.FirstFailureOrAllAsync(waits).ConfigureAwait(false);
        await AspireOwnedTaskJoin.CompleteAsync(lifetime, waits, primary).ConfigureAwait(false);
    }

    private static async Task WaitForNativeHealthyAsync(DistributedApplication app, string name, CancellationToken token)
        => await app.ResourceNotifications.WaitForResourceHealthyAsync(name,
            WaitBehavior.StopOnResourceUnavailable, token).ConfigureAwait(false);
}

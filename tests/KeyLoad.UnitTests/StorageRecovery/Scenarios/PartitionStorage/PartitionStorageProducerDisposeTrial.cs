using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageProducerDisposeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageProducerDisposeTrial
{
    internal static async Task<PartitionStorageProducerDisposeState> RunAsync(
        string[] arguments, LocalhostClusteringOptions localhost,
        RequestCqrsHostedProfile profile, CancellationToken token)
    {
        var failures = new List<Exception>();
        WebApplication? app = null;
        PartitionStorageProducerDisposeState? state = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(profile.Timing.StartupTimeout, TimeProvider.System);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, token);
            app = await ServerConfiguration.BuildAsync(arguments, localhostClustering: localhost,
                configureServices: profile.Register, startupCancellation: startup.Token).ConfigureAwait(false);
            var node = app.Services.GetRequiredService<OrleansNode>();
            await PartitionStorageStartupWaitAssertions.StartAsync(app, node, startup.Token).ConfigureAwait(false);
            state = Capture(app);
            await PartitionStorageProducerDisposeAssertions.BeforeAsync(state).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (app is not null)
        {
            Task? closing = null;
            await ServerFailureObserver.ObserveAsync(() => closing = app.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
            if (state is not null && closing is not null)
            {
                await ServerFailureObserver.ObserveAsync(() =>
                    PartitionStorageProducerDisposeAssertions.ClosedAsync(state, closing), failures).ConfigureAwait(false);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return state ?? throw new InvalidOperationException(P.Missing);
    }

    private static PartitionStorageProducerDisposeState Capture(WebApplication app)
    {
        var services = app.Services;
        var owner = services.GetRequiredService<PartitionHostApplicationOwner>();
        var host = owner.Borrow();
        return new(owner, services.GetRequiredService<OrleansNode>(), services.GetRequiredService<ServerConnectionRegistry>(),
            host.Database.Store.Read(_ => host.Database.Store.Identity),
            host.DirectoryPath);
    }
}

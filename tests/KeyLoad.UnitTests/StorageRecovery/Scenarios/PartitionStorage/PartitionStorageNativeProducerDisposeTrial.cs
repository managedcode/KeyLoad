using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Services;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProducerDisposeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeProducerDisposeTrial
{
    internal static async Task<PartitionStorageNativeProducerDisposeState> RunAsync(
        ControlledPartitionMovementNativeHostSettings settings, NativeLocalhostClusteringOptions localhost,
        ControlledPartitionMovementNativeHostEndpoints endpoints, CancellationToken token)
    {
        var failures = new List<Exception>();
        Task<WebApplication>? building = null;
        await ServerFailureObserver.ObserveAsync(() => building = ServerConfiguration.BuildAsync(
            settings.Arguments(P.Primary), localhostClustering: localhost, startupCancellation: token), failures)
            .ConfigureAwait(false);
        var app = building is { IsCompletedSuccessfully: true } ? await building.ConfigureAwait(false) : null;
        PartitionStorageNativeProducerDisposeState? state = null;
        if (app is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => StartAsync(app, endpoints, token), failures).ConfigureAwait(false);
            if (failures.Count == P.NoFailures)
            {
                ServerFailureObserver.Observe(() => state = Capture(app), failures);
                if (state is not null)
                {
                    await ServerFailureObserver.ObserveAsync(() =>
                        PartitionStorageNativeProducerDisposeAssertions.BeforeAsync(state), failures).ConfigureAwait(false);
                }
            }
            Task? closing = null;
            await ServerFailureObserver.ObserveAsync(() => closing = app.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
            if (state is not null && closing is not null)
            {
                await ServerFailureObserver.ObserveAsync(() =>
                    PartitionStorageNativeProducerDisposeAssertions.ClosedAsync(state, closing), failures).ConfigureAwait(false);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return state ?? throw new InvalidOperationException(P.Missing);
    }

    private static Task StartAsync(WebApplication app, ControlledPartitionMovementNativeHostEndpoints endpoints,
        CancellationToken token)
    {
        endpoints.Dispose();
        return app.StartAsync(token);
    }

    private static PartitionStorageNativeProducerDisposeState Capture(WebApplication app)
    {
        var services = app.Services;
        var owner = services.GetRequiredService<PartitionHostApplicationOwner>();
        var host = owner.Borrow();
        var native = services.GetServices<IGrainService>().ToArray();
        return new(owner, services.GetRequiredService<OrleansNode>(), services.GetRequiredService<ServerConnectionRegistry>(),
            native.OfType<RecurringDueGrainService>().Single(), native.OfType<SampleChunkGrainService>().Single(),
            native.OfType<AnnProjectionGrainService>().Single(), host.Database.Store.Read(_ => host.Database.Store.Identity),
            host.DirectoryPath);
    }
}

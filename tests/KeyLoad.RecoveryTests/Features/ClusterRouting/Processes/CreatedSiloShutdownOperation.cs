using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class CreatedSiloShutdownOperation
{
    private const int ExpectedStartupFailures = 1;
    private const string OwnedFile = "native-start-lifecycle.lock";

    internal static async Task RunAsync(string originalRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var path = Path.Combine(originalRoot, OwnedFile);
        var failures = new List<Exception>();
        var startup = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var service = new CreatedSiloEarlierService(path, selected);
            await ExecuteOwnedAsync(service, selected, path, startup, failures, cancellationToken);
        }, failures);
        if (failures.Count != 0)
        {
            failures.InsertRange(0, startup);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteOwnedAsync(CreatedSiloEarlierService service,
        CancellationTokenSource selected, string path, List<Exception> startup,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        IHost? host = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.SetMinimumLevel(global::Microsoft.Extensions.Logging.LogLevel.Warning);
            builder.Services.AddSingleton<IHostedService>(service);
            builder.UseOrleans(silo => silo.UseLocalhostClustering());
            global::KeyLoad.Server.Features.ClusterRouting.OrleansNativeHostedServiceRegistration.Guard(builder.Services);
            host = builder.Build();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => host.StartAsync(selected.Token), startup);
            await Assert.That(startup.Count).IsEqualTo(ExpectedStartupFailures);
            await Assert.That(ControlledPartitionMovementNativeFailedCreationFlow.HasOriginalCancellation(
                startup.Single(), service.StartupToken)).IsTrue();
            await Assert.That(selected.IsCancellationRequested).IsTrue();
            await Assert.That(service.StartupToken.IsCancellationRequested).IsTrue();
            await Assert.That(service.Opened).IsTrue();
            await Assert.That(service.StartEntered).IsFalse();
        }, failures);
        if (host is not null)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => host.StopAsync(cancellationToken), failures);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(service.StopEntered).IsTrue();
                await Assert.That(service.FileReleased).IsTrue();
                using var ownership = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }, failures);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                if (host is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    host.Dispose();
                }
            }, failures);
        }
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => service.DisposeAsync().AsTask(), failures);
        if (service.Opened && service.FileReleased)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => { File.Delete(path); return Task.CompletedTask; }, failures);
        }
    }
}

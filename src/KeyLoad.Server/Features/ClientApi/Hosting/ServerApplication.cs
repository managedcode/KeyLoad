using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class ServerApplication
{
    internal static async Task RunAsync(string[] args)
    {
        _ = SerializationExecutionRegistration.Process.Value;
        var app = ServerConfiguration.Build(args);
        var failures = new List<Exception>();
        PartitionHost? partition = null;
        OrleansNode? silo = null;
        INodeAdministration? administration = null;
        async Task RunLifetimeAsync()
        {
            try
            {
                partition = app.Services.GetRequiredService<PartitionHost>();
                administration = app.Services.GetRequiredService<INodeAdministration>();
                silo = app.Services.GetRequiredService<OrleansNode>();
                await partition.Coordinator.StartAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
                await app.StartAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
                await silo.StartAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
                await WaitForStopAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
        }
        await ServerFailureObserver.ObserveAsync(RunLifetimeAsync, failures).ConfigureAwait(false);
        await ShutdownAsync(app, silo, administration, partition, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaitForStopAsync(CancellationToken stopping)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = stopping.Register(static value => ((TaskCompletionSource)value!).TrySetResult(), signal);
        await signal.Task.ConfigureAwait(false);
    }

    private static async Task ShutdownAsync(WebApplication app, OrleansNode? silo, INodeAdministration? administration,
        PartitionHost? partition, List<Exception> failures)
    {
        var settings = app.Services.GetRequiredService<IOptions<ServerExecutionOptions>>().Value;
        using var deadline = new CancellationTokenSource(settings.ShutdownTimeout, app.Services.GetRequiredService<TimeProvider>());
        var connections = app.Services.GetRequiredService<ServerConnectionRegistry>();
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures).ConfigureAwait(false);
        if (silo is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => silo.StopAsync(deadline.Token), failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => silo.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
            if (!silo.HasJoinedRequestWork)
            {
                return;
            }
        }
        await ServerFailureObserver.ObserveAsync(() => app.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        if (administration is IAsyncDisposable borrowed)
        { await ServerFailureObserver.ObserveAsync(() => borrowed.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (partition is not null)
        { await ServerFailureObserver.ObserveAsync(() => partition.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
    }
}

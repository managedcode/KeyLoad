using System.Runtime.ExceptionServices;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class ServerApplication
{
    internal static async Task RunAsync(string[] args)
    {
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
        if (failures.Count == 1)
        { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1)
        { throw new AggregateException(failures); }
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
        using var deadline = new CancellationTokenSource(ServerProtocol.ShutdownTimeout);
        if (silo is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => silo.StopAsync(deadline.Token), failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => silo.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(() => app.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        if (administration is IAsyncDisposable borrowed)
        { await ServerFailureObserver.ObserveAsync(() => borrowed.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (partition is not null)
        { await ServerFailureObserver.ObserveAsync(() => partition.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
    }
}

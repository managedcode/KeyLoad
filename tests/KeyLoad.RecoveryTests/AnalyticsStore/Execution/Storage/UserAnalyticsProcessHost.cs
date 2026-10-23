using System.Net;
using System.Net.Sockets;
using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.RecoveryTests.Features.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.RecoveryTests.Features.AnalyticsStore;

/// <summary>Uses the production silo and Grain Service owner for local generation process recovery.</summary>
internal static class UserAnalyticsProcessHost
{
    internal static async Task RunAsync(string root, Func<PartitionHost, ServerRuntimeOptions, Task> body,
        CancellationToken token)
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var endpoint = (IPEndPoint)reservation.LocalEndpoint;
        var failures = new List<Exception>();
        WebApplication? application = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            application = await ServerConfiguration.BuildAsync(UserAnalyticsProcessOptions.Arguments(root, endpoint.Port),
                startupCancellation: token).ConfigureAwait(false);
            reservation.Stop();
            await application.StartAsync(token).ConfigureAwait(false);
            var host = application.Services.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
            await body(host, application.Services.GetRequiredService<ServerRuntimeOptions>()).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (application is not null)
        {
            await CloseAsync(application, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CloseAsync(WebApplication app, List<Exception> failures)
    {
        var runtime = app.Services.GetRequiredService<ServerRuntimeOptions>();
        using var deadline = new CancellationTokenSource(runtime.ServerExecution.Value.ShutdownTimeout, TimeProvider.System);
        var connections = app.Services.GetRequiredService<ServerConnectionRegistry>();
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures).ConfigureAwait(false);
        var node = app.Services.GetRequiredService<OrleansNode>();
        await ServerFailureObserver.ObserveAsync(() => node.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        if (!node.HasJoinedRequestWork)
        {
            failures.Add(new IOException(P.Invalid));
            return;
        }
        await ServerFailureObserver.ObserveAsync(() => app.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => app.Services.GetRequiredService<PartitionStorageLifecycle>()
            .StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        var owner = app.Services.GetRequiredService<PartitionHostApplicationOwner>();
        if (owner.Closing is not { IsCompleted: true } || !owner.CanReleaseRoot)
        {
            failures.Add(new IOException(P.Invalid));
            return;
        }
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
    }
}

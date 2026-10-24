using System.Net;
using KeyLoad.Clustering;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed class ConnectionShutdownKestrelOwner : IAsyncDisposable
{
    private const int IdleTimeoutSeconds = 20;
    private const string GuidFormat = "D";
    private readonly WebApplication app;
    private readonly SocketsHttpHandler handler;
    private static readonly Uri ConnectionRoute = new(string.Empty, UriKind.Relative);

    private ConnectionShutdownKestrelOwner(WebApplication app, ConnectionShutdownTerminal terminal, Uri address)
    {
        this.app = app;
        Terminal = terminal;
        Registry = app.Services.GetRequiredService<ServerConnectionRegistry>();
        handler = new();
        try
        {
            Client = new(handler, disposeHandler: false);
            ConfigureClient(address);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            if (Client is { } created)
            {
                ServerFailureObserver.Observe(created.Dispose, failures);
            }
            try
            {
                handler.Dispose();
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
            {
                failures.Add(failure);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void ConfigureClient(Uri address)
    {
        Client.BaseAddress = address;
        Client.DefaultRequestVersion = HttpVersion.Version11;
        Client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        Client.Timeout = Timeout.InfiniteTimeSpan;
    }

    internal ServerConnectionRegistry Registry
    {
        get;
    }
    internal HttpClient Client
    {
        get;
    }
    internal ConnectionShutdownTerminal Terminal
    {
        get;
    }

    internal static async Task<ConnectionShutdownKestrelOwner> StartAsync(Exception? original, CancellationToken token)
    {
        var terminal = new ConnectionShutdownTerminal(original);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var settings = new GrainRoutingOptions
        {
            ConnectionIdleTimeout = TimeSpan.FromSeconds(IdleTimeoutSeconds)
        };
        settings.Validate();
        builder.Services.AddSingleton<IOptions<GrainRoutingOptions>>(Options.Create(settings));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ServerConnectionRegistry>();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
            listen => listen.Use(next => context => terminal.RunAsync(context, next))));
        return await StartApplicationAsync(builder, terminal, token).ConfigureAwait(false);
    }

    private static async Task<ConnectionShutdownKestrelOwner> StartApplicationAsync(WebApplicationBuilder builder,
        ConnectionShutdownTerminal terminal, CancellationToken token)
    {
        var app = builder.Build();
        try
        {
            terminal.Bind(app.Services.GetRequiredService<ServerConnectionRegistry>());
            app.Run(context => context.Response.WriteAsync(
                context.Features.Get<ServerConnectionFeature>()!.Id.ToString(GuidFormat), context.RequestAborted));
            await app.StartAsync(token).ConfigureAwait(false);
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            return new(app, terminal, new Uri(addresses.Addresses.Single()));
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            terminal.Release();
            await ServerFailureObserver.ObserveAsync(() => app.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task<Guid> ReadConnectionAsync(CancellationToken token)
        => Guid.Parse(await Client.GetStringAsync(ConnectionRoute, token).ConfigureAwait(false));

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        Terminal.Release();
        try
        {
            ServerFailureObserver.Observe(Client.Dispose, failures);
        }
        finally
        {
            try
            {
                handler.Dispose();
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
            {
                failures.Add(failure);
            }
        }
        await ServerFailureObserver.ObserveAsync(() => Terminal.ObserveShutdownAsync(Registry, failures), failures)
            .ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => app.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

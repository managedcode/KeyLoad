using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class KeyLoadClientKestrelServer : IAsyncDisposable
{
    private const string AuthorizationHeader = "Authorization";
    private readonly WebApplication app;
    private KeyLoadClientKestrelServer(WebApplication app, Uri baseAddress, ConcurrentQueue<string> authorizationHeaders,
        KeyLoadClientKestrelObservation? observation)
    {
        this.app = app;
        Client = observation?.CreateClient(baseAddress)
            ?? new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        AuthorizationHeaders = authorizationHeaders;
    }

    public HttpClient Client { get; }
    public ConcurrentQueue<string> AuthorizationHeaders { get; }

    public static async Task<KeyLoadClientKestrelServer> StartAsync(RequestDelegate handler,
        KeyLoadClientKestrelObservation? observation = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        if (observation is not null)
        { builder.Services.AddSingleton<ILoggerProvider>(_ => new KeyLoadClientKestrelLogObservationProvider(observation)); }
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var authorizationHeaders = new ConcurrentQueue<string>();
        app.Run(async context =>
        {
            observation?.Record(KestrelObservationStage.HandlerEntered, token: context.RequestAborted);
            if (context.Request.Headers[AuthorizationHeader].SingleOrDefault() is { } authorization)
            {
                authorizationHeaders.Enqueue(authorization);
            }
            try
            {
                await handler(context);
                observation?.Record(KestrelObservationStage.HandlerCompleted, token: context.RequestAborted);
            }
            catch (Exception error)
            {
                observation?.Record(KestrelObservationStage.HandlerFailed, error: error, token: context.RequestAborted);
                throw;
            }
        });
        observation?.Record(KestrelObservationStage.HostStarting);
        await app.StartAsync();
        observation?.Record(KestrelObservationStage.HostStarted);
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            return new KeyLoadClientKestrelServer(app, new Uri(addresses.Addresses.Single()), authorizationHeaders, observation);
        }
        catch (Exception)
        {
            await app.StopAsync();
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

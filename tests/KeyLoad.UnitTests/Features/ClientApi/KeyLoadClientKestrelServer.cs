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
    private KeyLoadClientKestrelServer(WebApplication app, Uri baseAddress, ConcurrentQueue<string> authorizationHeaders)
    {
        this.app = app;
        Client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        AuthorizationHeaders = authorizationHeaders;
    }

    public HttpClient Client { get; }
    public ConcurrentQueue<string> AuthorizationHeaders { get; }

    public static async Task<KeyLoadClientKestrelServer> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var authorizationHeaders = new ConcurrentQueue<string>();
        app.Run(async context =>
        {
            if (context.Request.Headers[AuthorizationHeader].SingleOrDefault() is { } authorization)
            {
                authorizationHeaders.Enqueue(authorization);
            }
            await handler(context);
        });
        await app.StartAsync();
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            return new KeyLoadClientKestrelServer(app, new Uri(addresses.Addresses.Single()), authorizationHeaders);
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

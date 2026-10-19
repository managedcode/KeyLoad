using System.Net;
using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Owns the real ephemeral Kestrel host, caller, and request handler for execution header-start checks.</summary>
internal sealed class OperationHeaderTestServer : IAsyncDisposable
{
    private const string RequestIdHeaderName = ServerProtocol.RequestHeader;
    private const string MissingAddressFeature = "Kestrel did not expose its bound address feature.";
    private readonly WebApplication application;
    private readonly HttpClient client;
    private readonly OperationHeaderRequestHandler requestHandler;
    private readonly CancellationToken executionToken;

    private OperationHeaderTestServer(WebApplication application, HttpClient client,
        OperationHeaderRequestHandler requestHandler, CancellationToken executionToken)
    {
        this.application = application;
        this.client = client;
        this.requestHandler = requestHandler;
        this.executionToken = executionToken;
    }

    /// <summary>Starts a loopback Kestrel server and transfers ownership only after listener discovery succeeds.</summary>
    /// <param name="executionToken">The current TUnit execution cancellation token.</param>
    /// <param name="requireConcurrentRequests">Whether the request handler must observe both parallel callers.</param>
    /// <returns>The started owner of the host, HTTP caller and request handler.</returns>
    internal static async Task<OperationHeaderTestServer> StartAsync(CancellationToken executionToken,
        bool requireConcurrentRequests = false)
    {
        WebApplication? pendingApplication = null;
        HttpClient? pendingClient = null;
        OperationHeaderRequestHandler? pendingHandler = null;
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            pendingApplication = builder.Build();
            pendingHandler = new(requireConcurrentRequests);
            pendingApplication.Use(RegisterResponseHeaders);
            pendingApplication.Run(pendingHandler.HandleRequestAsync);
            using var startupTokenTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(OperationHeaderStartTests.OperationTimeoutSeconds), TimeProvider.System);
            using var startupToken = CancellationTokenSource.CreateLinkedTokenSource(executionToken, startupTokenTimeout.Token);
            await pendingApplication.StartAsync(startupToken.Token);
            var feature = pendingApplication.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                          ?? throw new InvalidOperationException(MissingAddressFeature);
            pendingClient = new HttpClient { BaseAddress = new Uri(feature.Addresses.Single()), Timeout = Timeout.InfiniteTimeSpan };
            var owner = new OperationHeaderTestServer(pendingApplication, pendingClient, pendingHandler, executionToken);
            pendingApplication = null;
            pendingClient = null;
            return owner;
        }
        finally
        {
            pendingClient?.Dispose();
            if (pendingApplication is not null)
            {
                await StopAndDisposeAsync(pendingApplication, executionToken);
            }
        }
    }

    /// <summary>Sends a real HTTP request with the execution token and a bounded per-request deadline.</summary>
    /// <param name="path">The loopback resource path.</param>
    /// <param name="executionToken">The current TUnit execution cancellation token.</param>
    /// <returns>The caller-visible HTTP response.</returns>
    internal async Task<HttpResponseMessage> GetAsync(string path, CancellationToken executionToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var requestTokenTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(OperationHeaderStartTests.OperationTimeoutSeconds), TimeProvider.System);
        using var requestToken = CancellationTokenSource.CreateLinkedTokenSource(executionToken, requestTokenTimeout.Token);
        return await client.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseContentRead, requestToken.Token);
    }

    /// <summary>Returns the actual request identity header or null when execution had not been published at start.</summary>
    /// <param name="response">The real Kestrel response.</param>
    /// <returns>The single N-format identity header, or null when absent.</returns>
    internal static string? RequestIdHeader(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValues(RequestIdHeaderName, out var values) ? values.SingleOrDefault() : null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        requestHandler.ReleaseParallelWaiters();
        await StopAndDisposeAsync(application, executionToken);
    }

    private static async Task RegisterResponseHeaders(HttpContext context, RequestDelegate next)
    {
        OperationResponseHeaders.Register(context);
        await next(context);
    }

    private static async Task StopAndDisposeAsync(WebApplication application, CancellationToken executionToken)
    {
        using var shutdownTokenTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(OperationHeaderStartTests.OperationTimeoutSeconds), TimeProvider.System);
        using var shutdownToken = CancellationTokenSource.CreateLinkedTokenSource(executionToken, shutdownTokenTimeout.Token);
        try
        {
            await application.StopAsync(shutdownToken.Token);
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
        }
        finally
        {
            await application.DisposeAsync();
        }
    }
}

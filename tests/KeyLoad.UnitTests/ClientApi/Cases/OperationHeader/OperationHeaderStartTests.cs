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

/// <summary>AC-MCP-003/005/007 and AC-ROUTE-001: native response start safely publishes request identities.</summary>
internal sealed class OperationHeaderStartTests
{
    internal const int OperationTimeoutSeconds = 10;
    internal const string BeforeStartPath = "/before-start";
    internal const string NoExecutionPath = "/no-execution";
    internal const string StartedBeforeExecutionPath = "/start-before-execution";
    internal const string ParallelPathPrefix = "/parallel/";
    internal const string ResponseBody = "operation-response";

    /// <summary>An identity published before native response start matches the caller-visible body and header.</summary>
    [Test]
    public async Task ExecutionIdentityBeforeStartCorrelatesHeaderAndBody()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var server = await OperationHeaderTestServer.StartAsync(cancellationToken);
        using var response = await server.GetAsync(BeforeStartPath, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var id = Guid.ParseExact(body, OrleansNodeProtocol.GuidFormat);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await Assert.That(body).IsEqualTo(id.ToString(OrleansNodeProtocol.GuidFormat));
        await Assert.That(OperationHeaderTestServer.RequestIdHeader(response)).IsEqualTo(body);
    }

    /// <summary>A response without a dispatched operation has no execution request ID header.</summary>
    [Test]
    public async Task ResponseWithoutExecutionHasNoRequestIdHeader()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var server = await OperationHeaderTestServer.StartAsync(cancellationToken);
        using var response = await server.GetAsync(NoExecutionPath, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsEqualTo(ResponseBody);
        await Assert.That(OperationHeaderTestServer.RequestIdHeader(response)).IsNull();
    }

    /// <summary>Publishing after native headers start preserves the response body and does not mutate committed headers.</summary>
    [Test]
    public async Task StartedResponseRemainsHealthyWhenIdentityArrivesLater()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var server = await OperationHeaderTestServer.StartAsync(cancellationToken);
        using var response = await server.GetAsync(StartedBeforeExecutionPath, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var id = Guid.ParseExact(body, OrleansNodeProtocol.GuidFormat);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await Assert.That(body).IsEqualTo(id.ToString(OrleansNodeProtocol.GuidFormat));
        await Assert.That(OperationHeaderTestServer.RequestIdHeader(response)).IsNull();
    }

    /// <summary>Overlapping native requests each return only their own published ID in the body and header.</summary>
    [Test]
    public async Task ConcurrentResponsesKeepRequestIdentitiesIsolated()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var server = await OperationHeaderTestServer.StartAsync(cancellationToken, requireConcurrentRequests: true);
        var identities = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var responses = await Task.WhenAll(identities.Select(async id =>
        {
            using var response = await server.GetAsync(
                $"{ParallelPathPrefix}{id.ToString(OrleansNodeProtocol.GuidFormat)}", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (Expected: id, response.StatusCode, Header: OperationHeaderTestServer.RequestIdHeader(response), Body: body);
        }));

        foreach (var result in responses)
        {
            var expected = result.Expected.ToString(OrleansNodeProtocol.GuidFormat);
            await Assert.That(result.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(result.Expected).IsNotEqualTo(Guid.Empty);
            await Assert.That(result.Header).IsEqualTo(expected);
            await Assert.That(result.Body).IsEqualTo(expected);
        }

        await Assert.That(responses[0].Header).IsNotEqualTo(responses[1].Header);
    }
}

/// <summary>Owns the real ephemeral Kestrel host, caller, and request handler for native header-start checks.</summary>
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

/// <summary>Publishes actual response IDs through one instance-owned concurrency handler for each Kestrel host.</summary>
internal sealed class OperationHeaderRequestHandler(bool requireConcurrentRequests)
{
    private const int ParallelRequestCount = 2;
    private const string ResponseNotStarted = "The native response did not start before identity publication.";
    private const string IdentityNotPublished = "The execution identity was not published.";
    private const string IdentityMismatch = "The published execution identity did not match the request.";
    private readonly TaskCompletionSource<bool>? parallelRequestsReady = requireConcurrentRequests
        ? new(TaskCreationOptions.RunContinuationsAsynchronously)
        : null;
    private int parallelRequestArrivals;

    /// <summary>Handles the four explicit real-network test routes.</summary>
    /// <param name="context">The current native Kestrel request.</param>
    /// <returns>Completion after the caller-visible response body is written.</returns>
    internal async Task HandleRequestAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Path == OperationHeaderStartTests.BeforeStartPath)
        {
            var id = Guid.NewGuid();
            OperationResponseHeaders.Publish(context, id);
            await WritePublishedIdentityAsync(context, id);
            return;
        }

        if (context.Request.Path == OperationHeaderStartTests.NoExecutionPath)
        {
            await context.Response.WriteAsync(OperationHeaderStartTests.ResponseBody, context.RequestAborted);
            return;
        }

        if (context.Request.Path == OperationHeaderStartTests.StartedBeforeExecutionPath)
        {
            await context.Response.StartAsync(context.RequestAborted);
            if (!context.Response.HasStarted)
            {
                throw new InvalidOperationException(ResponseNotStarted);
            }

            var id = Guid.NewGuid();
            OperationResponseHeaders.Publish(context, id);
            await WritePublishedIdentityAsync(context, id);
            return;
        }

        await WriteParallelIdentityAsync(context);
    }

    /// <summary>Releases a partial concurrency barrier before the owning host stops.</summary>
    public void ReleaseParallelWaiters() => parallelRequestsReady?.TrySetResult(true);

    private async Task WriteParallelIdentityAsync(HttpContext context)
    {
        if (!TryReadParallelId(context.Request.Path, out var id))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        OperationResponseHeaders.Publish(context, id);
        if (parallelRequestsReady is { } barrier)
        {
            if (Interlocked.Increment(ref parallelRequestArrivals) == ParallelRequestCount)
            {
                barrier.TrySetResult(true);
            }

            await barrier.Task.WaitAsync(context.RequestAborted);
        }

        await WritePublishedIdentityAsync(context, id);
    }

    private static bool TryReadParallelId(PathString path, out Guid id)
    {
        id = default;
        return path.Value is { } value
            && value.StartsWith(OperationHeaderStartTests.ParallelPathPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(value.AsSpan(OperationHeaderStartTests.ParallelPathPrefix.Length),
                OrleansNodeProtocol.GuidFormat, out id);
    }

    private static async Task WritePublishedIdentityAsync(HttpContext context, Guid expectedId)
    {
        var publishedId = OperationResponseHeaders.RequestId(context)
            ?? throw new InvalidOperationException(IdentityNotPublished);
        if (publishedId != expectedId || publishedId == Guid.Empty)
        {
            throw new InvalidOperationException(IdentityMismatch);
        }

        await context.Response.WriteAsync(publishedId.ToString(OrleansNodeProtocol.GuidFormat), context.RequestAborted);
    }
}

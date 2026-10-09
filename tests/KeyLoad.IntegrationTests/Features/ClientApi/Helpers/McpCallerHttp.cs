using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Creates real Aspire HTTP clients owned and disposed by each calling test scope.</summary>
internal static class McpCallerHttp
{
    /// <summary>Uses the actual RF3 endpoint while leaving request deadlines with the caller's token.</summary>
    /// <param name="fixture">The initialized real Docker/Aspire cluster.</param>
    /// <param name="node">The native Aspire resource name.</param>
    /// <returns>The disposable actual HTTP client, with no substituted handler.</returns>
    internal static HttpClient Create(ClusterFixture fixture, string node)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return Create(fixture.App, node);
    }

    /// <summary>Uses an actual endpoint from the caller-owned Aspire application wave.</summary>
    /// <param name="app">The genuine initialized Aspire application.</param>
    /// <param name="node">The native Aspire resource name.</param>
    /// <returns>The disposable HTTP client backed by the actual endpoint.</returns>
    internal static HttpClient Create(DistributedApplication app, string node)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        var http = app.CreateHttpClient(node, McpCallerProtocol.HttpEndpoint);
        http.Timeout = Timeout.InfiniteTimeSpan;
        return http;
    }
    /// <summary>Uses the same native default factory chain and discovered endpoint with passive initialization observation.</summary>
    internal static HttpClient CreateObserved(DistributedApplication app, string node, out McpInitializeHttpObservation observation)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        var endpoint = app.GetEndpoint(node, McpCallerProtocol.HttpEndpoint);
        var factory = app.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        observation = new(factory.CreateHandler(string.Empty));
        HttpClient? http = null;
        try
        {
            http = new HttpClient(observation, disposeHandler: false);
            var options = app.Services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(string.Empty);
            foreach (var configure in options.HttpClientActions)
            { configure(http); }
            http.BaseAddress = endpoint;
            http.Timeout = Timeout.InfiniteTimeSpan;
            return http;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            if (http is not null)
            { ServerFailureObserver.Observe(http.Dispose, failures); }
            ServerFailureObserver.Observe(observation.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

}

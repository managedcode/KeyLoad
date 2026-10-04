using Aspire.Hosting;
using Aspire.Hosting.Testing;

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
}

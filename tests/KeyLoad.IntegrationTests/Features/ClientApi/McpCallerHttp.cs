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
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        var http = fixture.App.CreateHttpClient(node, McpCallerProtocol.HttpEndpoint);
        http.Timeout = Timeout.InfiniteTimeSpan;
        return http;
    }
}

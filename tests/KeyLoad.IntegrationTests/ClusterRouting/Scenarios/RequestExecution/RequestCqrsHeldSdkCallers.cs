using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Uses the same actual Aspire endpoint and default factory chain with a passive send observer.</summary>
internal static class RequestCqrsHeldSdkCallers
{
    internal static async Task<(RequestCqrsRf3Callers Caller, RequestCqrsHeldSdkObservation? Observation)> ConnectAsync(
        DistributedApplication app, string node, string credential, bool officialMcp, TimeProvider clock, CancellationToken token)
    {
        if (officialMcp)
        {
            return (await RequestCqrsRf3Callers.ConnectAsync(app, node, credential, token).ConfigureAwait(false), null);
        }
        var factory = app.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        var observation = new RequestCqrsHeldSdkObservation(factory.CreateHandler(string.Empty), clock);
        var http = new HttpClient(observation, disposeHandler: true);
        try
        {
            var options = app.Services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(string.Empty);
            foreach (var configure in options.HttpClientActions)
            {
                configure(http);
            }
            http.BaseAddress = app.GetEndpoint(node, McpCallerProtocol.HttpEndpoint);
            http.Timeout = Timeout.InfiniteTimeSpan;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(http.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        var caller = await RequestCqrsRf3Callers.ConnectObservedAsync(app, node, credential, http, token)
            .ConfigureAwait(false);
        return (caller, observation);
    }
}

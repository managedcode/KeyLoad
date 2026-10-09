using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnWaitObservedSdk
{
    internal static HttpClient Create(DistributedApplication app, string node,
        out NativeAnnWaitHttpObservation observation, out NativeAnnWaitHttpHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        var endpoint = app.GetEndpoint(node, McpCallerProtocol.HttpEndpoint);
        var factory = app.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        observation = new();
        handler = new NativeAnnWaitHttpHandler(factory.CreateHandler(string.Empty), observation);
        HttpClient? http = null;
        try
        {
            http = new HttpClient(handler, disposeHandler: false);
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
            ServerFailureObserver.Observe(handler.Dispose, failures);
            handler = null;
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}


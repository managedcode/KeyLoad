using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3ResponseCall
{
    internal static async Task<CommitInboxResult?> SendAsync(ClusterFixture fixture, MessagingRf3Identity identity, CommitInboxRequest request,
        List<Exception> failures, CancellationToken token)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var factory = fixture.App.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        using var observer = new TargetInboxRf3ResponseObservation(factory.CreateHandler(string.Empty), request.CommandId, caller.CancelAsync);
        using var http = new HttpClient(observer, disposeHandler: false);
        CommitInboxResult? receipt = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var options = fixture.App.Services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(string.Empty);
            foreach (var configure in options.HttpClientActions)
            { configure(http); }
            http.BaseAddress = fixture.App.GetEndpoint(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint);
            http.Timeout = Timeout.InfiniteTimeSpan;
            var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
            var actual = await sdk.CommitInboxAsync(request, caller.Token);
            await Assert.That(observer.Observed).IsTrue();
            await Assert.That(caller.IsCancellationRequested).IsTrue();
            if (actual.IsSuccess)
            { receipt = actual.Value; }
            else
            { await TargetInboxRf3Assertions.DeniedAsync(actual, ErrorCode.UnknownWriteOutcome); }
        }, failures).ConfigureAwait(false);
        return receipt;
    }
}

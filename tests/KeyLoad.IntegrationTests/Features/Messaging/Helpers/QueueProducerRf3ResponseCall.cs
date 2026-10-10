using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3ResponseCall
{
    internal static async Task<CommitReceipt?> SendAsync(ClusterFixture fixture, QueueProducerRf3Seed seed,
        bool cancelAfterResponse, List<Exception> failures, CancellationToken token)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var factory = fixture.App.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        using var observer = new QueueProducerRf3ResponseObservation(factory.CreateHandler(string.Empty), seed.Original.CommandId, caller.CancelAsync);
        using var http = new HttpClient(observer, disposeHandler: false);
        CommitReceipt? receipt = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var options = fixture.App.Services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(string.Empty);
            foreach (var configure in options.HttpClientActions)
            { configure(http); }
            http.BaseAddress = fixture.App.GetEndpoint(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint);
            http.Timeout = Timeout.InfiniteTimeSpan;
            var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
            if (!cancelAfterResponse)
            {
                using var ordinaryHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
                receipt = await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(ordinaryHttp,
                    seed.Identity.Secret, IntegrationClientOptions.Execution()).CommitAsync(seed.Original, token));
                await Assert.That(observer.Observed).IsFalse();
                return;
            }
            var actual = await sdk.CommitAsync(seed.Original, caller.Token);
            await Assert.That(observer.Observed).IsTrue();
            await Assert.That(caller.IsCancellationRequested).IsTrue();
            if (actual.IsSuccess)
            { receipt = actual.Value; }
            else
            { await QueueProducerRf3Assertions.DeniedAsync(actual, ErrorCode.UnknownWriteOutcome); }
        }, failures).ConfigureAwait(false);
        return receipt;
    }
}

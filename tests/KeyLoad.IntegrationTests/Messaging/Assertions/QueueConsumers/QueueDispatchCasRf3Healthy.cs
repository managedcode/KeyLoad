using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasRf3Healthy
{
    internal static Task<QueueDispatchCasRf3State> RunAsync(ClusterFixture fixture, QueueDispatchCasRf3State state,
        CancellationToken token)
        => QueueDispatchCasRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async (sdk, mcp) =>
        {
            await AdminDispatchQueueVerifier.DeliverAndAcknowledgeAsync(sdk, mcp, state.Lane, token);
            var message = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
                new(state.Lane, QueueDispatchCasRf3Protocol.Message), token)) ?? throw new InvalidOperationException();
            await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Acked);
            await Assert.That(message.PayloadJson).IsNull();
            await Assert.That(message.HeadersJson).IsNull();
            var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.BrowseQueueAsync(new(state.Lane), token));
            return state with
            {
                Message = message,
                Counters = JsonDefaults.Serialize(page.Counters),
                Items = JsonDefaults.Serialize(page.Items)
            };
        }, token);
}

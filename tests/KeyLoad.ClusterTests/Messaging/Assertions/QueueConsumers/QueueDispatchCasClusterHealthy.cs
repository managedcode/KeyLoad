using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDispatchCasClusterHealthy
{
    internal static Task<QueueDispatchCasClusterState> RunAsync(ClusterFixture fixture, QueueDispatchCasClusterState state,
        CancellationToken token)
        => QueueDispatchCasClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async (sdk, mcp) =>
        {
            await AdminDispatchQueueVerifier.DeliverAndAcknowledgeAsync(sdk, mcp, state.Lane, token);
            var message = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
                new(state.Lane, QueueDispatchCasClusterProtocol.Message), token)) ?? throw new InvalidOperationException();
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

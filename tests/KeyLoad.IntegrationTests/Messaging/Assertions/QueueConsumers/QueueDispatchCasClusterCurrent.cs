using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDispatchCasClusterCurrent
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueDispatchCasClusterState state,
        long version, bool paused, CancellationToken token)
    {
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
                new(state.Lane.Partition.TenantId, state.Lane.Partition.DatabaseId), token));
            var queue = page.Items.Single(item => string.Equals(item.Name, state.Lane.Queue, StringComparison.Ordinal));
            await Assert.That(queue.Kind).IsEqualTo(state.Definition.Kind);
            await Assert.That(queue.TransactionDomainId).IsEqualTo(state.Definition.TransactionDomainId);
            await Assert.That(queue.SchemaVersion).IsEqualTo(version);
            await Assert.That(queue.Paused).IsEqualTo(paused);
            var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Lane.Partition, token));
            await Assert.That(JsonDefaults.Serialize(outbox).AsSpan().SequenceEqual(JsonDefaults.Serialize(state.Outbox))).IsTrue();
            var mcpOutbox = await McpCallerAssertions.SuccessAsync<OutboxStatus>(await admin.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Lane.Partition), token));
            await Assert.That(JsonDefaults.Serialize(mcpOutbox.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(state.Outbox))).IsTrue();
            await QueueDeadlineClusterAssertions.RequireAsync(admin, state.Lane, state.Message, token);
            var browse = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.BrowseQueueAsync(new(state.Lane), token));
            await Assert.That(JsonDefaults.Serialize(browse.Counters).AsSpan().SequenceEqual(state.Counters)).IsTrue();
            await Assert.That(JsonDefaults.Serialize(browse.Items).AsSpan().SequenceEqual(state.Items)).IsTrue();
            return true;
        }, token);
    }
}

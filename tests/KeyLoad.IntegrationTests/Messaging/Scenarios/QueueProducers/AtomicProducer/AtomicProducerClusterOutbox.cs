using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicProducerClusterOutbox
{
    private const long FirstSequence = 1L;

    internal static async Task<OutboxStatus> ReadAsync(ClusterFixture fixture, QueueProducerClusterSeed seed,
        CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, token);
        var status = await McpCallerAssertions.SdkSuccessAsync(await administrator.OutboxStatusAsync(seed.Lane.Partition, token));
        var official = (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await mcp.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(seed.Lane.Partition), token))).Value;
        await QueueProducerClusterAssertions.EqualAsync(official, status);
        return status;
    }

    internal static async Task LiteralAsync(OutboxStatus status, long mutations)
    {
        await Assert.That(status.Head.Tail).IsEqualTo(mutations);
        await Assert.That(status.Head.FirstAvailable).IsEqualTo(FirstSequence);
        await Assert.That(status.Head.StoredRecords).IsEqualTo(mutations);
        await Assert.That(status.Head.StoredBytes > 0L).IsTrue();
        await Assert.That(status.Consumers).IsEmpty();
    }

    internal static async Task SameAsync(ClusterFixture fixture, QueueProducerClusterSeed seed,
        OutboxStatus expected, CancellationToken token)
        => await QueueProducerClusterAssertions.EqualAsync(await ReadAsync(fixture, seed, token), expected);
}

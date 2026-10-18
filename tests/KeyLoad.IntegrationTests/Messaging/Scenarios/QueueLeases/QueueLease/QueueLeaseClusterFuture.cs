using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseClusterFuture
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueLeaseClusterSeed seed, CancellationToken token)
    {
        var before = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
            new(seed.FutureLane, QueueLeaseClusterProtocol.FutureMessage), token))
            ?? throw new InvalidOperationException(QueueLeaseClusterProtocol.MissingSetup);
        await QueueLeaseClusterAssertions.EqualAsync(before, seed.FutureSnapshot);
        await Assert.That(before.Metadata.State).IsEqualTo(MessageState.Scheduled);
        await Assert.That(before.Metadata.NotBefore).IsEqualTo(seed.FutureDue);
        await Assert.That(before.Metadata.LeaseOwner).IsNull();
        await Assert.That(before.Metadata.LeaseUntil).IsNull();
        await Assert.That(before.PayloadJson).IsEqualTo(QueueLeaseClusterProtocol.Payload);
        await Assert.That(before.HeadersJson).IsEqualTo(QueueLeaseClusterProtocol.Headers);
        await Assert.That(seed.FutureDue).IsGreaterThan(TimeProvider.System.GetUtcNow());
        var request = new ReceiveRequest(Guid.NewGuid(), seed.FutureLane);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
        await Assert.That(empty.Deliveries).IsEmpty();
        var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token));
        await QueueLeaseClusterAssertions.EqualAsync(replay.Value, empty);
        var after = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
            new(seed.FutureLane, QueueLeaseClusterProtocol.FutureMessage), token));
        await QueueLeaseClusterAssertions.EqualAsync(after, before);
    }
}

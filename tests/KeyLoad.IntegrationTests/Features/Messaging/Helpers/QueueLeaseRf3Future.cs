using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseRf3Future
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueLeaseRf3Seed seed, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(seed.FutureLane, QueueLeaseRf3Protocol.FutureMessage), token))
            ?? throw new InvalidOperationException(QueueLeaseRf3Protocol.MissingSetup);
        await QueueLeaseRf3Assertions.EqualAsync(before, seed.FutureSnapshot);
        await Assert.That(before.Metadata.State).IsEqualTo(MessageState.Scheduled);
        await Assert.That(before.Metadata.NotBefore).IsEqualTo(seed.FutureDue);
        await Assert.That(before.Metadata.LeaseOwner).IsNull();
        await Assert.That(before.Metadata.LeaseUntil).IsNull();
        await Assert.That(before.PayloadJson).IsEqualTo(QueueLeaseRf3Protocol.Payload);
        await Assert.That(before.HeadersJson).IsEqualTo(QueueLeaseRf3Protocol.Headers);
        await Assert.That(seed.FutureDue).IsGreaterThan(TimeProvider.System.GetUtcNow());
        var request = new ReceiveRequest(Guid.NewGuid(), seed.FutureLane);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
        await Assert.That(empty.Deliveries).IsEmpty();
        var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token));
        await QueueLeaseRf3Assertions.EqualAsync(replay.Value, empty);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(seed.FutureLane, QueueLeaseRf3Protocol.FutureMessage), token));
        await QueueLeaseRf3Assertions.EqualAsync(after, before);
    }
}

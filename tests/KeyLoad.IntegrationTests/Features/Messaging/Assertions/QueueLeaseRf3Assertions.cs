using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseRf3Assertions
{
    internal static async Task<MessageInspection> InspectAsync(KeyLoadClient sdk, QueueLaneRef lane, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(lane, QueueLeaseRf3Protocol.Message), token));
        return actual ?? throw new InvalidOperationException(QueueLeaseRf3Protocol.MissingSetup);
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task DeniedAsync<T>(Result<T> actual, ErrorCode expected)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value).IsNull();
        await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }

    internal static async Task LiteralDeliveryAsync(Delivery delivery, string id, string payload)
    {
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLeaseRf3Protocol.Headers);
    }

    internal static async Task LeasedAsync(MessageInspection actual, Delivery delivery, string principal)
    {
        await Assert.That(actual.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(actual.Metadata.Id).IsEqualTo(delivery.Id);
        await Assert.That(actual.Metadata.LeaseOwner).IsEqualTo(principal);
        await Assert.That(actual.Metadata.LeaseVersion).IsEqualTo(delivery.LeaseVersion);
        await Assert.That(actual.Metadata.LeaseUntil).IsEqualTo(delivery.LeaseUntil);
        await Assert.That(actual.Metadata.DeliveryGeneration).IsEqualTo(delivery.DeliveryGeneration);
        await Assert.That(actual.Metadata.Attempts).IsEqualTo(delivery.Attempt);
        await Assert.That(actual.PayloadJson).IsEqualTo(delivery.PayloadJson);
        await Assert.That(actual.HeadersJson).IsEqualTo(delivery.HeadersJson);
    }

    internal static async Task ReplayedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        DeliveryCommand command, CommitReceipt expected, CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(command, token)), expected);
        var observed = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, command, token));
        await EqualAsync(observed.Value, expected);
    }
}

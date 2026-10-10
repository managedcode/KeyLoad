using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingRf3Assertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task DeniedAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }

    internal static async Task UnchangedAsync(KeyLoadClient client, InboxProcessingRf3State state, CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(
            new(state.Lane, InboxProcessingRf3Protocol.Message), token)), state.Input);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(
            new(new(state.Lane.Partition, InboxProcessingRf3Protocol.Output), InboxProcessingRf3Protocol.Message), token)), state.Output);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(
            new(state.Lane.Partition, InboxProcessingRf3Protocol.Collection, InboxProcessingRf3Protocol.Document), token)), state.Document);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(
            new(state.Lane.Partition, InboxProcessingRf3Protocol.Collection, InboxProcessingRf3Protocol.Refused), token))).IsNull();
    }
}

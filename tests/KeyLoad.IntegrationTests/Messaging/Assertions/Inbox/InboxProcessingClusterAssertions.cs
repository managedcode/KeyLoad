using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingClusterAssertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task DeniedAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }

    internal static async Task UnchangedAsync(KeyLoadClient client, InboxProcessingClusterState state, CancellationToken token)
    {
        await EqualAsync(await PublicReadAssertions.MessageAsync(await client.InspectAsync(
            new(state.Lane, InboxProcessingClusterProtocol.Message), token)), state.Input);
        await EqualAsync(await PublicReadAssertions.MessageAsync(await client.InspectAsync(
            new(new(state.Lane.Partition, InboxProcessingClusterProtocol.Output), InboxProcessingClusterProtocol.Message), token)), state.Output);
        await EqualAsync(await PublicReadAssertions.DocumentAsync(await client.GetAsync(
            new(state.Lane.Partition, InboxProcessingClusterProtocol.Collection, InboxProcessingClusterProtocol.Document), token)), state.Document);
        await Assert.That(await PublicReadAssertions.DocumentAsync(await client.GetAsync(
            new(state.Lane.Partition, InboxProcessingClusterProtocol.Collection, InboxProcessingClusterProtocol.Refused), token))).IsNull();
    }
}

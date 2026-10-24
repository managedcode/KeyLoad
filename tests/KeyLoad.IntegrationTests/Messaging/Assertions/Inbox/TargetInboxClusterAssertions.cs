using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class TargetInboxClusterAssertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task DeniedAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }
    internal static async Task UnchangedAsync(KeyLoadClient client, TargetInboxClusterState state, CancellationToken token)
    {
        await EqualAsync(await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(state.Source, TargetInboxClusterProtocol.Message), token)), state.Input);
        await EqualAsync(await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(new(state.Target.Partition,
            TargetInboxClusterProtocol.Output), TargetInboxClusterProtocol.Message), token)), state.Output);
        await EqualAsync(await PublicReadAssertions.DocumentAsync(await client.GetAsync(new(state.Target.Partition,
            TargetInboxClusterProtocol.Collection, TargetInboxClusterProtocol.Document), token)), state.Document);
        await Assert.That(await PublicReadAssertions.DocumentAsync(await client.GetAsync(new(state.Target.Partition,
            TargetInboxClusterProtocol.Collection, TargetInboxClusterProtocol.Changed), token))).IsNull();
    }
}

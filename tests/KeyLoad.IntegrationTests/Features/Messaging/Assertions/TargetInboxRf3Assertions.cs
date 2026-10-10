using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3Assertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task DeniedAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }
    internal static async Task UnchangedAsync(KeyLoadClient client, TargetInboxRf3State state, CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(state.Source, TargetInboxRf3Protocol.Message), token)), state.Input);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(new(state.Target.Partition,
            TargetInboxRf3Protocol.Output), TargetInboxRf3Protocol.Message), token)), state.Output);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(new(state.Target.Partition,
            TargetInboxRf3Protocol.Collection, TargetInboxRf3Protocol.Document), token)), state.Document);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(new(state.Target.Partition,
            TargetInboxRf3Protocol.Collection, TargetInboxRf3Protocol.Changed), token))).IsNull();
    }
}

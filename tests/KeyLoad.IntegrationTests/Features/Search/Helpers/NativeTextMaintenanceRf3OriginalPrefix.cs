using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3OriginalPrefix
{
    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TextIndexMaintenanceRequest original, TextIndexMaintenanceResult built,
        NativeTextMaintenanceRf3Scenario scenario, NativeTextMaintenancePath path, CancellationToken token)
    {
        var baseline = JsonDefaults.Serialize(await OutboxAsync(sdk, mcp, scenario.Partition, token));
        var current = await OutboxAsync(sdk, mcp, scenario.Partition, token);
        await Assert.That(JsonDefaults.Serialize(current).SequenceEqual(baseline)).IsTrue();
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, original, path, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await NativeTextMaintenanceRf3Assertions.ResultAsync(replay, original);
        await Assert.That(built.Checkpoint).IsNotNull();
        await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(replay, built);
        await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(replay.Source!, built.Source!,
            before, after, current.Head.Tail);
        await Assert.That(replay.IndexedThroughSequence).IsEqualTo(built.IndexedThroughSequence);
        await Assert.That(replay.IndexSha256).IsEqualTo(built.IndexSha256);
        await Assert.That(replay.Source!.ThroughSequence).IsGreaterThan(replay.IndexedThroughSequence!.Value);
        var settled = await OutboxAsync(sdk, mcp, scenario.Partition, token);
        await Assert.That(JsonDefaults.Serialize(settled).SequenceEqual(baseline)).IsTrue();
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
    }

    private static async Task<OutboxStatus> OutboxAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(partition, token));
        var official = (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await mcp.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value;
        await Assert.That(JsonDefaults.Serialize(official).SequenceEqual(JsonDefaults.Serialize(direct))).IsTrue();
        return direct;
    }
}

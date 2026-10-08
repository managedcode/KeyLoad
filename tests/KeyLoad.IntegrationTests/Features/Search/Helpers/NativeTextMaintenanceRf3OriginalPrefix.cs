using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3OriginalPrefix
{
    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TextIndexMaintenanceRequest original, TextIndexMaintenanceResult built,
        NativeTextMaintenancePath path, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, original, path, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await NativeTextMaintenanceRf3Assertions.ResultAsync(replay, original);
        await Assert.That(replay.IndexedThroughSequence).IsEqualTo(built.IndexedThroughSequence);
        await Assert.That(replay.IndexSha256).IsEqualTo(built.IndexSha256);
        await Assert.That(replay.Source!.ThroughSequence).IsGreaterThan(replay.IndexedThroughSequence!.Value);
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
    }
}

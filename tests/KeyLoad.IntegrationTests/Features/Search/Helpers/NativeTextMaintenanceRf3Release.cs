using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Release
{
    private const int EmptyRecords = 0;
    private const string ReleasedDetail = "The projection generation was released.";

    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest originalRestore,
        NativeTextMaintenancePath path, CancellationToken token)
    {
        var release = originalRestore with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Release };
        var actual = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, release, path, token);
        await Assert.That(actual.CommandId).IsEqualTo(release.CommandId);
        await Assert.That(actual.Consumer).IsEqualTo(release.Consumer);
        await Assert.That(actual.IndexGeneration).IsEqualTo(release.IndexGeneration);
        await Assert.That(actual.Source).IsNull();
        await Assert.That(actual.Checkpoint).IsNull();
        await Assert.That(actual.IndexSha256).IsNull();
        await Assert.That(actual.IndexedThroughSequence).IsNull();
        await Assert.That(actual.TrackedRecords).IsEqualTo(EmptyRecords);
        await Assert.That(actual.ReleasedConsumer!.Consumer).IsEqualTo(release.Consumer);
        await Assert.That(actual.ReleasedConsumer.Released).IsTrue();
        await Assert.That(actual.ReleasedConsumer.Definition.IndexGeneration).IsEqualTo(release.IndexGeneration);
        var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, release, path, token);
        await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(actual))).IsTrue();
        var denied = await sdk.MaintainTextIndexAsync(originalRestore, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.TokenInvalidated.ToString());
        await Assert.That(denied.Problem?.Detail).IsEqualTo(ReleasedDetail);
        var official = await mcp.CallAsync(TextIndexMaintenanceProtocol.ToolName, originalRestore, token);
        _ = await McpCallerAssertions.ErrorAsync(official, ErrorCode.TokenInvalidated, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(ReleasedDetail);
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextOnlineRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenancePath path, CancellationToken token)
    {
        var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        await scenario.SeedAsync(sdk, token);
        var source = await scenario.RequestAsync(sdk, mcp, token);
        var request = new OnlineTextIndexMaintenanceRequest(source.CommandId, source.Consumer, source.Collection,
            source.Field, source.IndexGeneration, source.NodeId, source.Placement);
        var configuration = new ConfigureProjectionConsumerRequest(Guid.NewGuid(), request.Consumer,
            new(request.ConsumerGeneration, [request.Collection], ["putDocument",
                "patchDocument", "deleteDocument"]));
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureProjectionAsync(configuration, token));
        var originalOutbox = JsonDefaults.Serialize(await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scenario.Partition, token)));
        foreach (var candidate in NativeTextOnlineRf3Assertions.Paths)
        {
            await NativeTextOnlineRf3Call.RejectedAsync(sdk, mcp, request with { NodeId = Guid.NewGuid() }, candidate, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scenario.Partition, token));
            await Assert.That(JsonDefaults.Serialize(after).SequenceEqual(originalOutbox)).IsTrue();
        }
        var original = await NativeTextOnlineRf3Call.ExecuteAsync(sdk, mcp, request, path, token);
        await NativeTextOnlineRf3Assertions.ResultAsync(original, request, false);
        await NativeTextOnlineRf3Assertions.LiteralAllAsync(sdk, mcp, scenario, false, token);
        await NativeTextOnlineRf3Assertions.ReplayAllAsync(sdk, mcp, request, original, token);
        var mutation = await NativeTextMaintenanceRf3Mutation.CommitAsync(sdk, scenario, token);
        var successor = request with { CommandId = Guid.NewGuid() };
        var changed = await NativeTextOnlineRf3Call.ExecuteAsync(sdk, mcp, successor, path, token);
        await NativeTextOnlineRf3Assertions.ResultAsync(changed, successor, true);
        await Assert.That(changed.BaseCut.ThroughSequence).IsGreaterThan(original.PublishedCut.ThroughSequence);
        await NativeTextOnlineRf3Assertions.LiteralAllAsync(sdk, mcp, scenario, true, token);
        await NativeTextMaintenanceRf3Mutation.ReplayAsync(sdk, mcp, mutation, token);
        await NativeTextOnlineRf3Assertions.ReplayAllAsync(sdk, mcp, request, original, token);
        await NativeTextOnlineRf3Assertions.ReplayAllAsync(sdk, mcp, successor, changed, token);
        await NativeTextOnlineRf3Assertions.LiteralAllAsync(sdk, mcp, scenario, true, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await NativeTextOnlineRf3Cold.RunAsync(fixture, scenario, request, original, successor, changed, before, token);
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextOnlineRf3Assertions
{
    private const long TrackedBilingualRecords = 2;
    internal static readonly NativeTextMaintenancePath[] Paths =
        [NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp, NativeTextMaintenancePath.SdkSql, NativeTextMaintenancePath.McpSql];

    internal static async Task ResultAsync(OnlineTextIndexMaintenanceResult result, OnlineTextIndexMaintenanceRequest request, bool changed)
    {
        await Assert.That(result.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(result.Consumer).IsEqualTo(request.Consumer);
        await Assert.That(result.ConsumerGeneration).IsEqualTo(request.ConsumerGeneration);
        _ = changed;
        await Assert.That(result.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        await Assert.That(result.BaseCut.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(result.PublishedCut.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(result.PublishedCut.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(result.PublishedCut.ReadGeneration).IsEqualTo(result.BaseCut.ReadGeneration);
        await Assert.That(result.PublishedCut.AppliedPosition).IsGreaterThanOrEqualTo(result.BaseCut.AppliedPosition);
        await Assert.That(result.Checkpoint.Checkpoint).IsEqualTo(result.PublishedCut.ThroughSequence);
        await Assert.That(result.Checkpoint.Receipt.Mutations).IsEmpty();
        await Assert.That(result.Checkpoint.Receipt.Token.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(result.Checkpoint.Receipt.Token.AtomicPartitionId).IsEqualTo(request.Consumer.Partition.AtomicPartitionId);
        await Assert.That(result.Checkpoint.Receipt.Token.OwnershipEpoch).IsEqualTo(request.Placement.PlacementEpoch);
    }

    internal static async Task ReplayAllAsync(KeyLoadClient sdk, McpOfficialClient mcp, OnlineTextIndexMaintenanceRequest request,
        OnlineTextIndexMaintenanceResult original, CancellationToken token)
    {
        foreach (var path in Paths)
        {
            var actual = await NativeTextOnlineRf3Call.ExecuteAsync(sdk, mcp, request, path, token);
            await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        }
    }

    internal static async Task LiteralAllAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, bool changed, CancellationToken token)
    {
        var request = new SearchRequest(scenario.Partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.Field, changed ? "CHANGED" : "ПРИВІТ");
        RankedDocument[] expected = [new(new(new(scenario.Partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.Ukrainian), changed ? 2 : 1,
            changed ? NativeTextMaintenanceRf3Scenario.ChangedJson : NativeTextMaintenanceRf3Scenario.UkrainianJson, false, []), 1d / 61d)];
        foreach (var path in Paths)
        {
            var actual = await NativeTextSelectedRf3Call.ExecuteAsync(sdk, mcp, request, path, token);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            var english = await NativeTextSelectedRf3Call.ExecuteAsync(sdk, mcp, request with { Text = "hello" }, path, token);
            RankedDocument[] expectedEnglish = changed ? [] : [new(new(new(scenario.Partition,
                NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.English), 1,
                NativeTextMaintenanceRf3Scenario.EnglishJson, false, []), 1d / 61d)];
            await Assert.That(JsonDefaults.Serialize(english).SequenceEqual(JsonDefaults.Serialize(expectedEnglish))).IsTrue();
            if (changed)
            { await Assert.That(await NativeTextSelectedRf3Call.ExecuteAsync(sdk, mcp, request with { Text = "ПРИВІТ" }, path, token)).IsEmpty(); }
        }
    }
}

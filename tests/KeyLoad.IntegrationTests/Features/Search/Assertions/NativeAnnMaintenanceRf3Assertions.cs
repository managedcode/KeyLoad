using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnMaintenanceRf3Assertions
{
    private const int Count = 3;
    private const long Revision = 1;
    private static readonly double[] Scores = [1d / 61, 1d / 62, 1d / 63];

    internal static async Task CanonicalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeAnnMaintenanceRf3Scenario scenario, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(scenario.Search(), token));
        var official = (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Search(), token))).Value;
        await Assert.That(NativeSerialization.Serialize(official)).IsEquivalentTo(NativeSerialization.Serialize(direct), CollectionOrdering.Matching);
        await Assert.That(direct.Length).IsEqualTo(Count);
        for (var index = 0; index < Count; index++)
        {
            var row = direct[index];
            await Assert.That(row.Document.Reference).IsEqualTo(new EntityRef(scenario.Partition,
                NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[index]));
            await Assert.That(row.Document.Json).IsEqualTo(NativeAnnMaintenanceRf3Scenario.Json);
            await Assert.That(row.Document.Revision).IsEqualTo(Revision);
            await Assert.That(row.Document.Redacted).IsFalse();
            await Assert.That(row.Document.RedactedFields).IsEmpty();
            await Assert.That(row.Score).IsEqualTo(Scores[index]);
            await Assert.That(row.Explanation).IsNull();
        }
    }
    internal static async Task ResultAsync(AnnMaintenanceResult result, AnnMaintenanceRequest request)
    {
        await Assert.That(result.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(result.Consumer).IsEqualTo(request.Consumer);
        await Assert.That(result.IndexGeneration).IsEqualTo(request.IndexGeneration);
        await Assert.That(result.Phase).IsEqualTo(AnnMaintenancePhase.Completed);
        await Assert.That(result.Count).IsEqualTo(Count);
        await Assert.That(result.Source!.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(result.Source.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(result.IndexSha256).IsNotNull();
        await Assert.That(result.Checkpoint).IsNotNull();
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Assertions
{
    private const int Count = 2;
    private const long FirstRevision = 1;
    private const long ChangedRevision = 2;
    private const double Score = 1d / 61;
    private const string OriginalUkrainianQuery = "ПРИВІТ";
    private const string OriginalEnglishQuery = "hello";
    private const string ChangedQuery = "CHANGED";

    internal static async Task ResultAsync(TextIndexMaintenanceResult result, TextIndexMaintenanceRequest request)
    {
        await Assert.That(result.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(result.Consumer).IsEqualTo(request.Consumer);
        await Assert.That(result.IndexGeneration).IsEqualTo(request.IndexGeneration);
        await Assert.That(result.Phase).IsEqualTo(TextIndexMaintenancePhase.Completed);
        await Assert.That(result.TrackedRecords).IsEqualTo(Count);
        await Assert.That(result.Source!.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(result.Source.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(result.IndexSha256).IsNotNull();
        await Assert.That(result.IndexedThroughSequence).IsNotNull();
        await Assert.That(result.ReleasedConsumer).IsNull();
    }

    internal static async Task CanonicalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, bool changed, CancellationToken token)
    {
        var reference = new EntityRef(scenario.Partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.Ukrainian);
        var expected = new DocumentResult(reference, changed ? ChangedRevision : FirstRevision,
            changed ? NativeTextMaintenanceRf3Scenario.ChangedJson : NativeTextMaintenanceRf3Scenario.UkrainianJson, false, []);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, token));
        var official = (await McpCallerAssertions.SuccessAsync<DocumentResult>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value;
        await Assert.That(JsonDefaults.Serialize(direct).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(official).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var search = new SearchRequest(scenario.Partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.Field, changed ? ChangedQuery : OriginalUkrainianQuery);
        RankedDocument[] literal = [new(expected, Score)];
        var rows = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(search, token));
        var officialRows = (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, search, token))).Value;
        await Assert.That(JsonDefaults.Serialize(rows).SequenceEqual(JsonDefaults.Serialize(literal))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(officialRows).SequenceEqual(JsonDefaults.Serialize(literal))).IsTrue();
        if (changed)
        {
            await EmptyAsync(sdk, mcp, search with { Text = OriginalUkrainianQuery }, token);
            await EmptyAsync(sdk, mcp, search with { Text = OriginalEnglishQuery }, token);
            var removed = new EntityRef(scenario.Partition, NativeTextMaintenanceRf3Scenario.Collection,
                NativeTextMaintenanceRf3Scenario.English);
            await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(removed, token))).IsNull();
            await Assert.That((await McpCallerAssertions.SuccessAsync<DocumentResult?>(await mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(removed), token))).Value).IsNull();
        }
    }

    private static async Task EmptyAsync(KeyLoadClient sdk, McpOfficialClient mcp, SearchRequest request,
        CancellationToken token)
    {
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, token))).IsEmpty();
        await Assert.That((await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, request, token))).Value).IsEmpty();
    }
}

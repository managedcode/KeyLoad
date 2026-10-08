using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedRf3Assertions
{
    private const string OriginalQuery = "ПРИВІТ";
    private const string ChangedQuery = "CHANGED";
    private const string MissingEnglish = "hello";
    private const long OriginalRevision = 1;
    private const long ChangedRevision = 2;
    private const double Score = 1d / 61d;

    internal static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest maintenance,
        NativeTextMaintenancePath path, bool changed, CancellationToken token)
    {
        var request = Request(scenario, maintenance, changed ? ChangedQuery : OriginalQuery);
        var actual = await NativeTextSelectedRf3Call.ExecuteAsync(sdk, mcp, request, path, token);
        RankedDocument[] expected = [new(new(new(scenario.Partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.Ukrainian), changed ? ChangedRevision : OriginalRevision,
            changed ? NativeTextMaintenanceRf3Scenario.ChangedJson : NativeTextMaintenanceRf3Scenario.UkrainianJson,
            false, []), Score)];
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        if (changed)
        {
            await Assert.That(await NativeTextSelectedRf3Call.ExecuteAsync(sdk, mcp,
                request with { Text = OriginalQuery }, path, token)).IsEmpty();
            await Assert.That(await NativeTextSelectedRf3Call.ExecuteAsync(sdk, mcp,
                request with { Text = MissingEnglish }, path, token)).IsEmpty();
        }
    }

    internal static async Task StaleAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest maintenance,
        NativeTextMaintenancePath path, CancellationToken token)
    {
        await NativeTextSelectedRf3Rejection.AssertAsync(sdk, mcp, Request(scenario, maintenance, ChangedQuery), path, token);
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
    }

    private static SearchRequest Request(NativeTextMaintenanceRf3Scenario scenario,
        TextIndexMaintenanceRequest maintenance, string query)
        => new(scenario.Partition, NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.Field,
            query, TextIndex: new(maintenance.Consumer, maintenance.IndexGeneration));
}

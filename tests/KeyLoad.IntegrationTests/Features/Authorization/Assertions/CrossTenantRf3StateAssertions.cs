using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CrossTenantRf3StateAssertions
{
    private const string Scan = "SELECT * FROM \"mcp-documents\" ORDER BY id ASC LIMIT 10";
    private const string Indexed = "SELECT * FROM \"mcp-documents\" WHERE secret = 'mcp-private-value-canary' ORDER BY id ASC LIMIT 10";
    internal static QueryRequest Query(PartitionRef partition, bool indexed)
        => new(partition, indexed ? Indexed : Scan, AllowFullScan: true);
    internal static async Task StateAsync(KeyLoadClient admin, McpDocumentScenario scenario, string json,
        long revision, CancellationToken token)
    {
        var document = await McpCallerAssertions.SdkSuccessAsync(await admin.GetAsync(scenario.Reference, token));
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Reference).IsEqualTo(scenario.Reference);
        await Assert.That(document.Revision).IsEqualTo(revision);
        await Assert.That(document.Json).IsEqualTo(json);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        var scan = await McpCallerAssertions.SdkSuccessAsync(await admin.QueryAsync(Query(scenario.Partition, false), token));
        await PageAsync(scan, json, revision, "bounded-full-scan");
        var indexed = await McpCallerAssertions.SdkSuccessAsync(await admin.QueryAsync(Query(scenario.Partition, true), token));
        await Assert.That(indexed.AccessPath).IsEqualTo("index:secret");
        await Assert.That(indexed.Cursor).IsNull();
        if (json == McpDocumentProtocol.InitialJson)
        { await PageAsync(indexed, json, revision, "index:secret"); }
        else
        {
            await Assert.That(indexed.Rows).IsEmpty();
            var positive = Query(scenario.Partition, true) with
            { Sql = Indexed.Replace(McpDocumentProtocol.PrivateValue, CrossTenantRf3WholeFlow.HealthyCanary, StringComparison.Ordinal) };
            await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.QueryAsync(positive, token)), json, revision, "index:secret");
        }
    }
    private static async Task PageAsync(QueryPage page, string json, long revision, string access)
    {
        await Assert.That(page.AccessPath).IsEqualTo(access);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.Rows).HasSingleItem();
        var row = page.Rows[0];
        await Assert.That(row.EntityId).IsEqualTo(McpDocumentProtocol.Entity);
        await Assert.That(row.Revision).IsEqualTo(revision);
        await Assert.That(row.Json).IsEqualTo(json);
        await Assert.That(row.Redacted).IsFalse();
        await Assert.That(row.RedactedFields ?? []).IsEmpty();
        await Assert.That(row.Sources).IsNull();
    }
}

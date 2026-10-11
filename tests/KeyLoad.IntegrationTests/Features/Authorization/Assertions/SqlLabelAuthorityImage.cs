using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SqlLabelAuthorityImage
{
    internal static async Task<JsonElement> CaptureAsync(KeyLoadClient root, PartitionRef partition, CancellationToken token)
    {
        var documents = new List<DocumentResult>();
        foreach (var row in new (string Collection, string Id, string Json)[] {
            ("graphprojects", "root", "{\"name\":\"root\"}"),
            ("graphprojects", "second-seed", "{\"name\":\"second\"}"),
            ("graphprojects", "middle", "{\"name\":\"middle\"}"),
            ("graphprojects", "context", "{\"name\":\"context\"}"),
            ("graphdocs", "alpha", "{\"name\":\"alpha\"}"),
            ("graphdocs", "beta", "{\"name\":\"beta\"}"),
            ("graphdocs", "gamma", "{\"name\":\"gamma\"}") })
        {
            var reference = new EntityRef(partition, row.Collection, row.Id);
            var actual = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(reference, token));
            await Assert.That(actual).IsNotNull();
            await SqlRf3Protocol.EqualAsync(new DocumentResult(reference, McpCallerProtocol.FirstRevision,
                row.Json, false, []), actual);
            if (actual is not null)
            { documents.Add(actual); }
        }
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(partition, token));
        return JsonSerializer.SerializeToElement(new { Documents = documents, Outbox = outbox }, JsonDefaults.Options);
    }
}

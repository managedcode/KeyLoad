using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.Search;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SqlLabelAuthorityImage
{
    internal static async Task<JsonElement> CaptureAsync(KeyLoadClient root, GraphSearchClusterScenario scenario, CancellationToken token)
    {
        var partition = scenario.Partition;
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
            var actual = await PublicReadAssertions.DocumentAsync(await root.GetAsync(reference, token));
            await Assert.That(actual).IsNotNull();
            await SqlClusterProtocol.EqualAsync(new DocumentResult(scenario.StoredVertex(row.Collection, row.Id), McpCallerProtocol.FirstRevision,
                row.Json, false, []), actual);
            if (actual is not null)
            {
                documents.Add(actual);
            }
        }
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(partition, token));
        return JsonSerializer.SerializeToElement(new
        {
            Documents = documents,
            Outbox = outbox
        }, JsonDefaults.Options);
    }
}

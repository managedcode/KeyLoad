using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCursorRequestIdentityR47Seed
{
    internal static Task<QueryCursorRequestIdentityR47State> RunAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin =>
            {
                var original = await SqlOffsetClusterSetup.RunAsync(fixture, admin, token);
                var request = SqlOffsetClusterProtocol.ScalarRequest(original.Partition);
                return await CaptureAsync(admin, original, request with
                {
                    Sql = request.Sql.Replace("OFFSET 1", "OFFSET 0", StringComparison.Ordinal)
                }, token);
            }, token);

    private static async Task<QueryCursorRequestIdentityR47State> CaptureAsync(
        RequestCqrsClusterCallers admin, SqlOffsetClusterState original,
        QueryRequest changedRequest, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(original.Partition, changedRequest.Partition);
        await Assert.That(changedRequest.Cursor).IsNull();
        var complete = SqlOffsetClusterProtocol.ScalarRequest(original.Partition) with
        {
            Sql = "SELECT TOP(4) title FROM offsetleft ORDER BY order_id"
        };
        var resources = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(original.Partition.TenantId, original.Partition.DatabaseId), token));
        return new(original.Identity.Secret, original.Partition,
            SqlOffsetClusterProtocol.ScalarRequest(original.Partition), changedRequest, complete,
            Rows(), Documents(original.Partition),
            [resources.Items.Single(item => item.Name == SqlOffsetClusterProtocol.Left),
             resources.Items.Single(item => item.Name == SqlOffsetClusterProtocol.Right)],
            "bounded-full-scan", [original.Seed], [original.Receipt],
            await QueryCursorRequestIdentityR47Current.OutboxAsync(admin, original.Partition, token), []);
    }

    private static QueryRow[] Rows() =>
    [
        new("a", 1, """{"title":"first"}""", false, []),
        new("b", 1, """{"title":"second"}""", false, []),
        new("c", 1, """{"title":"third"}""", false, []),
        new("d", 1, """{"title":"fourth"}""", false, [])
    ];

    private static DocumentResult[] Documents(PartitionRef partition) =>
    [
        new(new(partition, SqlOffsetClusterProtocol.Left, "a"), 1, """{"order_id":"a","customer_id":"c1","title":"first"}""", false, []),
        new(new(partition, SqlOffsetClusterProtocol.Left, "b"), 1, """{"order_id":"b","customer_id":"c2","title":"second"}""", false, []),
        new(new(partition, SqlOffsetClusterProtocol.Left, "c"), 1, """{"order_id":"c","customer_id":"c3","title":"third"}""", false, []),
        new(new(partition, SqlOffsetClusterProtocol.Left, "d"), 1, """{"order_id":"d","customer_id":"c4","title":"fourth"}""", false, []),
        new(new(partition, SqlOffsetClusterProtocol.Right, "c1"), 1, """{"id":"c1","name":"Aye"}""", false, []),
        new(new(partition, SqlOffsetClusterProtocol.Right, "c2"), 1, """{"id":"c2","name":"Bee"}""", false, []),
        new(new(partition, SqlOffsetClusterProtocol.Right, "c3"), 1, """{"id":"c3","name":"See"}""", false, []),
        new(new(partition, SqlOffsetClusterProtocol.Right, "c4"), 1, """{"id":"c4","name":"Dee"}""", false, [])
    ];
}

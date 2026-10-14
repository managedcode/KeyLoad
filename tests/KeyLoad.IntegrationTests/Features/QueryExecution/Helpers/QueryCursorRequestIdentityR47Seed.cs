using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCursorRequestIdentityR47Seed
{
    internal static Task<QueryCursorRequestIdentityR47State> RunAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin =>
            {
                var original = await SqlOffsetRf3Setup.RunAsync(fixture, admin, token);
                var request = SqlOffsetRf3Protocol.ScalarRequest(original.Partition);
                return await CaptureAsync(admin, original, request with
                { Sql = request.Sql.Replace("OFFSET 1", "OFFSET 0", StringComparison.Ordinal) }, token);
            }, token);

    private static async Task<QueryCursorRequestIdentityR47State> CaptureAsync(
        RequestCqrsRf3Callers admin, SqlOffsetRf3State original,
        QueryRequest changedRequest, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(original.Partition, changedRequest.Partition);
        await Assert.That(changedRequest.Cursor).IsNull();
        var complete = SqlOffsetRf3Protocol.ScalarRequest(original.Partition) with
        { Sql = "SELECT TOP(4) title FROM offsetleft ORDER BY order_id" };
        var resources = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(original.Partition.TenantId, original.Partition.DatabaseId), token));
        return new(original.Identity.Secret, original.Partition,
            SqlOffsetRf3Protocol.ScalarRequest(original.Partition), changedRequest, complete,
            Rows(), Documents(original.Partition),
            [resources.Items.Single(item => item.Name == SqlOffsetRf3Protocol.Left),
             resources.Items.Single(item => item.Name == SqlOffsetRf3Protocol.Right)],
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
        new(new(partition, SqlOffsetRf3Protocol.Left, "a"), 1, """{"order_id":"a","customer_id":"c1","title":"first"}""", false, []),
        new(new(partition, SqlOffsetRf3Protocol.Left, "b"), 1, """{"order_id":"b","customer_id":"c2","title":"second"}""", false, []),
        new(new(partition, SqlOffsetRf3Protocol.Left, "c"), 1, """{"order_id":"c","customer_id":"c3","title":"third"}""", false, []),
        new(new(partition, SqlOffsetRf3Protocol.Left, "d"), 1, """{"order_id":"d","customer_id":"c4","title":"fourth"}""", false, []),
        new(new(partition, SqlOffsetRf3Protocol.Right, "c1"), 1, """{"id":"c1","name":"Aye"}""", false, []),
        new(new(partition, SqlOffsetRf3Protocol.Right, "c2"), 1, """{"id":"c2","name":"Bee"}""", false, []),
        new(new(partition, SqlOffsetRf3Protocol.Right, "c3"), 1, """{"id":"c3","name":"See"}""", false, []),
        new(new(partition, SqlOffsetRf3Protocol.Right, "c4"), 1, """{"id":"c4","name":"Dee"}""", false, [])
    ];
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicProducerRf3Index
{
    internal const string Name = "work";
    internal const string Field = "/work";
    private const string AccessPath = "index:work";
    private const string OriginalSql = "SELECT * FROM producers WHERE work = 1";
    private const string HealthySql = "SELECT * FROM producers WHERE work = 2";

    internal static async Task RequireAsync(ClusterFixture fixture, QueueProducerRf3Seed seed,
        bool healthy, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await PageAsync(sdk, mcp, new(seed.Lane.Partition, OriginalSql),
            [new(QueueProducerRf3Protocol.Original, QueueProducerRf3Protocol.InitialRevision, QueueProducerRf3Protocol.Payload, false, [])], token);
        QueryRow[] expected = healthy
            ? [new(QueueProducerRf3Protocol.Healthy, QueueProducerRf3Protocol.InitialRevision, QueueProducerRf3Protocol.HealthyPayload, false, [])] : [];
        await PageAsync(sdk, mcp, new(seed.Lane.Partition, HealthySql), expected, token);
    }

    private static async Task PageAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueryRequest request,
        QueryRow[] expected, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, request, token))).Value;
        foreach (var page in new[] { direct, official })
        {
            await QueueProducerRf3Assertions.EqualAsync(page.Rows.ToArray(), expected);
            await Assert.That(page.AccessPath).IsEqualTo(AccessPath);
            await Assert.That(page.Cursor).IsNull();
        }
    }
}

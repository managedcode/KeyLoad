using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Query;
using KeyLoad.Server;
namespace KeyLoad.ClusterTests.Messaging;

internal static class CompetingInboxPublicInventory
{
    private const string QueueScanPath = "model-scan:queue-messages";
    internal static async Task RunAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var resource in new[]
        {
            CompetingInboxPublicProtocol.Documents,
            CompetingInboxPublicProtocol.Input,
            CompetingInboxPublicProtocol.Output
        })
        {
            await ServerFailureObserver.ObserveAsync(() => ResourceAsync(state, root, resource, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ResourceAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        string resource, CancellationToken token)
    {
        var queue = resource != CompetingInboxPublicProtocol.Documents;
        var source = queue ? new ModelQuerySource(ModelQuerySourceKind.QueueMessages, resource) : null;
        var request = new AstQueryRequest(state.Partition, new(resource, null,
            [new(CompetingInboxPublicProtocol.Star, CompetingInboxPublicProtocol.Star)], null, [],
            CompetingInboxPublicProtocol.CompleteInventoryPage, ModelSource: source), AllowFullScan: true);
        var first = resource == CompetingInboxPublicProtocol.Input ? CompetingInboxPublicProtocol.First : CompetingInboxPublicProtocol.Effect;
        var next = resource == CompetingInboxPublicProtocol.Input ? CompetingInboxPublicProtocol.Next : CompetingInboxPublicProtocol.Healthy;
        string[] expected = state.HasHealthy ? [first, next] : [first];
        var failures = new List<Exception>();
        foreach (var official in new[]
        {
            false,
            true
        })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var page = official
                    ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await root.Mcp.CallAsync(McpCallerTools.QueryAst, request, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(request, token));
                await Assert.That(page.CutPosition).IsGreaterThan(0L);
                await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(state.Producers.Select(value => value.Receipt.Token.Position)
                    .Concat(state.Current.Select(value => value.Receipt.Token.Position)).Append(state.Winner.Token.Position).Max());
                await Assert.That(page.Cursor).IsNull();
                var rows = expected.Order(StringComparer.Ordinal).Select(id => Row(state, resource, id)).ToArray();
                await CompetingInboxPublicValues.EqualAsync(rows, page.Rows.ToArray());
                var expectedPage = new QueryPage([.. rows], null, page.CutPosition,
                    queue ? QueueScanPath : CompetingInboxPublicProtocol.ScanPath);
                await CompetingInboxPublicValues.EqualAsync(page, expectedPage);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static QueryRow Row(CompetingInboxPublicState state, string resource, string id)
    {
        var json = id is CompetingInboxPublicProtocol.Healthy ? CompetingInboxPublicProtocol.HealthyJson : state.Json(state.WinnerIndex);
        if (resource == CompetingInboxPublicProtocol.Documents)
        {
            return new(id, 1L, json, false, []);
        }
        var input = resource == CompetingInboxPublicProtocol.Input;
        var value = input
            ? "{\"id\":\"" + id + "\",\"state\":\"Acked\",\"attempts\":1,\"stateVersion\":3,\"notBefore\":null,\"expiresAt\":null,\"payload\":null,\"headers\":null}"
            : "{\"id\":\"" + id + "\",\"state\":\"Ready\",\"attempts\":0,\"stateVersion\":1,\"notBefore\":null,\"expiresAt\":null,\"payload\":" + json + ",\"headers\":{}}";
        return new(id, input ? 3L : 1L, value, false, []);
    }
}

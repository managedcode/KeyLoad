using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresErrorReceiverHealthy
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state, CancellationToken token)
    {
        await PostgresRf3Assertions.MinimumTokenAsync(fixture, state, token);
        await PostgresRf3Parity.RunAsync(fixture, state, token);
        await PostgresRf3Receipts.RunAsync(fixture, state, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
        {
            var request = Ast(state);
            await PostgresRf3Assertions.PageAsync(state, await McpCallerAssertions.SdkSuccessAsync(
                await reader.Sdk.QueryAstAsync(request, token)));
            await PostgresRf3Assertions.PageAsync(state, (await McpCallerAssertions.SuccessAsync<QueryPage>(
                await reader.Mcp.CallAsync(McpCallerTools.QueryAst, request, token))).Value);
            return true;
        }, token);
    }

    private static global::KeyLoad.Query.AstQueryRequest Ast(PostgresRf3State state)
    {
        global::KeyLoad.Query.SelectQuery select = state.Dialect == PostgresRf3Protocol.ScalarDialect
            ? new(PostgresRf3State.Left.Collection, null, [new("*", "*")], null, [new("order_id", false)], Limit)
            : new(PostgresRf3State.Left.Collection, "l",
                [new("order_id", "order_id", "l"), new("detail", "left_detail", "l"),
                    new("name", "right_name", "r"), new("id", "customer_id", "r")],
                null, [new("l.order_id", false)], Limit,
                InnerJoin: new(PostgresRf3State.Right.Collection, "r", "customer_id", "id"));
        return new(PostgresRf3State.Partition, select, AllowFullScan: true,
            AstVersion: state.Dialect == PostgresRf3Protocol.JoinDialect ? JoinAstVersion : ScalarAstVersion);
    }
    private const int Limit = 10;
    private const int ScalarAstVersion = 1;
    private const int JoinAstVersion = 2;
}

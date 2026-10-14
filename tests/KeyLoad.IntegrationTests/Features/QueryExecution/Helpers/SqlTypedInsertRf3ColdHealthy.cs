using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3ColdHealthy
{
    internal static async Task<SqlTypedInsertRf3State> RunAsync(ClusterFixture fixture,
        SqlTypedInsertRf3State state, bool final, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async caller =>
        {
            var request = SqlTypedInsertRf3Literal.Request(Guid.NewGuid(),
                final ? SqlTypedInsertRf3Literal.SecondColdSql : SqlTypedInsertRf3Literal.FirstColdSql);
            var receipt = await SqlRf3Protocol.McpAsync<CommitReceipt>(caller.Mcp, request, token);
            await SqlTypedInsertRf3Hot.ReceiptAsync(request, receipt, state.Owner,
                new MutationReceipt(SqlTypedInsertRf3Literal.PutKind, SqlTypedInsertRf3Literal.Table,
                    final ? SqlTypedInsertRf3Literal.SecondCold : SqlTypedInsertRf3Literal.FirstCold,
                    SqlTypedInsertRf3Literal.InitialRevision));
            return state with
            {
                ColdRows = checked(state.ColdRows + NextCold),
                ColdReceipts = [.. state.ColdReceipts ?? [], new(request, receipt)]
            };
        }, token);
    private const int NextCold = 1;
}

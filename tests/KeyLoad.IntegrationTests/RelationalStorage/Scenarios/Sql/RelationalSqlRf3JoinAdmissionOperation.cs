using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class RelationalSqlRf3JoinAdmissionOperation
{
    internal static async Task<RelationalQ2CancellationOutcome> StartAsync(
        ClusterRouting.RequestCqrsRf3Callers caller, RelationalQ2AdmissionSeed seed,
        bool officialMcp, bool ast, CancellationToken token)
    {
        try
        {
            if (officialMcp)
            {
                if (ast)
                {
                    await caller.Mcp.CallAsync(McpCallerTools.QueryAst, seed.Ast, token);
                }
                else
                {
                    await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, seed.Sql, token);
                }
                return new(true, null, null);
            }
            if (ast)
            {
                var result = await caller.Sdk.QueryAstAsync(seed.Ast, token);
                await Assert.That(result.Value).IsNull();
                return new(result.IsSuccess, result.Problem?.ErrorCode, null);
            }
            var sql = await caller.Sdk.ExecuteSqlAsync(seed.Sql, token);
            await Assert.That(sql.Value).IsNull();
            return new(sql.IsSuccess, sql.Problem?.ErrorCode, null);
        }
        catch (OperationCanceledException cancellation) when (token.IsCancellationRequested)
        {
            return new(false, null, cancellation);
        }
    }
}

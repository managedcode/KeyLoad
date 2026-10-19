using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class ModelRootClusterWave
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        ModelRootClusterState state, bool use, bool read, CancellationToken token)
    {
        await ModelRootCluster.RunAsync(root, reader, state, read, token);
        foreach (var explicitValue in new[] { false, true })
        {
            await ObserveAsync(root, state, async () => await ModelRootClusterResults.PageAsync(
                await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(state.Sql(explicitValue: explicitValue), token)), state, read, explicitValue), token);
            await ObserveAsync(root, state, async () => await ModelRootClusterResults.PageAsync(
                await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(state.Ast(explicitValue: explicitValue), token)), state, read, explicitValue), token);
            await ObserveAsync(root, state, async () => await ModelRootClusterResults.PageAsync(
                (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute,
                    state.Sql(explicitValue: explicitValue), token))).Value, state, read, explicitValue), token);
            await ObserveAsync(root, state, async () => await ModelRootClusterResults.PageAsync(
                (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst,
                    state.Ast(explicitValue: explicitValue), token))).Value, state, read, explicitValue), token);
        }
        await ObserveAsync(root, state, async () =>
        {
            var sql = await reader.Sdk.QueryAsync(state.Sql(filter: true), token);
            var ast = await reader.Sdk.QueryAstAsync(state.Ast(filter: true), token);
            if (use)
            {
                await ModelRootClusterResults.PageAsync(await McpCallerAssertions.SdkSuccessAsync(sql), state, read, false);
                await ModelRootClusterResults.PageAsync(await McpCallerAssertions.SdkSuccessAsync(ast), state, read, false);
            }
            else
            {
                await Assert.That(sql.IsSuccess).IsFalse();
                await Assert.That(ast.IsSuccess).IsFalse();
                await Assert.That(sql.Value).IsNull();
                await Assert.That(ast.Value).IsNull();
                var problem = Errors.Problem(ErrorCode.PermissionDenied, "The query requires a protected field-use grant.");
                await SqlClusterProtocol.EqualAsync(problem, sql.Problem);
                await SqlClusterProtocol.EqualAsync(problem, ast.Problem);
            }
        }, token);
        foreach (var ast in new[] { false, true })
        {
            await ObserveAsync(root, state, async () =>
            {
                var reply = await reader.Mcp.CallAsync(ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                    ast ? (object)state.Ast(filter: true) : state.Sql(filter: true), token);
                if (use)
                {
                    await ModelRootClusterResults.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, state, read, false);
                }
                else
                {
                    await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
                }
            }, token);
        }
    }

    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, ModelRootClusterState state, Func<Task> operation, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(state.Ast(), token));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(before,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(state.Ast(), token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(outbox,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token))), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

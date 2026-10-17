using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Concurrency
{
    private const int First = 0;
    private const int Second = 1;
    internal static async Task RunAsync(ClusterFixture fixture, SqlCreateTableRf3State state, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            var first = SqlCreateTableRf3Literal.Request(Guid.NewGuid(), SqlCreateTableRf3Literal.ConcurrentSql);
            var second = SqlCreateTableRf3Literal.Request(Guid.NewGuid(), SqlCreateTableRf3Literal.ConcurrentSql);
            var results = await Task.WhenAll(caller.Sdk.ExecuteSqlAsync(first, token), caller.Sdk.ExecuteSqlAsync(second, token));
            await Assert.That(results[First].IsSuccess != results[Second].IsSuccess).IsTrue();
            var winner = results[First].IsSuccess ? First : Second;
            var loser = winner == First ? Second : First;
            var success = winner == First ? first : second;
            var failure = loser == First ? first : second;
            await Assert.That(results[loser].Problem).IsNotNull();
            await Assert.That(results[loser].Problem!.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
            var expected = SqlCreateTableRf3Literal.Definition(SqlCreateTableRf3Literal.ConcurrentTable);
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SdkSuccessAsync(results[winner]))
                .Deserialize<ResourceDefinition>(JsonDefaults.Options)!);
            await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<ResourceDefinition>(caller.Sdk, success, token));
            await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<ResourceDefinition>(caller.Mcp, success, token));
            await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, failure, ErrorCode.Conflict, token);
            await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, failure, ErrorCode.Conflict, dispatched: true, token);
            return true;
        }, token);
}

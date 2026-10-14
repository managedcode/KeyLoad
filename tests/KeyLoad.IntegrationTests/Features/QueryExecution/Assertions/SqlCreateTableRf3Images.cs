using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Images
{
    internal static async Task<SqlCreateTableRf3Image> ReadAsync(KeyLoadClient root, CancellationToken token)
        => new(await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(
            new(SqlCreateTableRf3Literal.Partition, SqlCreateTableRf3Literal.Table, SqlCreateTableRf3Literal.One), token)),
            await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(SqlCreateTableRf3Literal.Partition, token)));
    internal static async Task RequireAsync(KeyLoadClient root, SqlCreateTableRf3Image before, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(before, await ReadAsync(root, token));
}

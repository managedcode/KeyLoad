using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3Images
{
    internal static async Task<SqlTypedInsertRf3Image> ReadAsync(KeyLoadClient root, CancellationToken token)
    {
        var values = new List<DocumentReadResult>(Ids.Length);
        foreach (var id in Ids)
        {
            values.Add(await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(new(SqlTypedInsertRf3Literal.Partition, SqlTypedInsertRf3Literal.Table, id), token)));
        }
        return new(values.ToArray(), await McpCallerAssertions.SdkSuccessAsync(
            await root.OutboxStatusAsync(SqlTypedInsertRf3Literal.Partition, token)));
    }
    internal static async Task RequireAsync(KeyLoadClient root, SqlTypedInsertRf3Image before, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(before, await ReadAsync(root, token));
    private static readonly string[] Ids = [SqlTypedInsertRf3Literal.One, SqlTypedInsertRf3Literal.Two,
        SqlTypedInsertRf3Literal.Three, SqlTypedInsertRf3Literal.Four, SqlTypedInsertRf3Literal.Five,
        SqlTypedInsertRf3Literal.Cancelled, SqlTypedInsertRf3Literal.FirstCold, SqlTypedInsertRf3Literal.SecondCold];
}

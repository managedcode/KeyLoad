using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class SqlTypedInsertMovementPages
{
    internal static Task ReceiptAsync(SqlOperationRequest request, CommitReceipt receipt,
        AtomicPartitionPlacementResolution owner, string id)
        => SqlTypedInsertRf3Hot.ReceiptAsync(request, receipt, owner,
            new MutationReceipt(SqlTypedInsertMovementProtocol.Put, SqlTypedInsertMovementProtocol.Table, id, SqlTypedInsertMovementProtocol.Revision));
    internal static async Task RowsAsync(KeyLoadClient root, RequestCqrsRf3Callers caller,
        PostgresRf3Caller wire, PartitionRef partition, bool healthy, CommitReceipt minimum, CancellationToken token)
    {
        var expected = new List<QueryRow> { new(SqlTypedInsertMovementProtocol.One,SqlTypedInsertMovementProtocol.Revision,
            SqlTypedInsertMovementProtocol.OneJson,false,[]) };
        if (healthy)
        {
            expected.Add(new(SqlTypedInsertMovementProtocol.Two, SqlTypedInsertMovementProtocol.Revision,
            SqlTypedInsertMovementProtocol.TwoJson, false, []));
        }
        var request = new SqlOperationRequest(partition, SqlTypedInsertMovementProtocol.ReadSql, AllowFullScan: true);
        await PageAsync(expected, await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, request, token));
        await PageAsync(expected, await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, request, token));
        await PageAsync(expected, await wire.ExecuteAsync<QueryPage>(request.Sql, [], prepared: true, token));
        var id = healthy ? SqlTypedInsertMovementProtocol.Two : SqlTypedInsertMovementProtocol.One;
        var json = healthy ? SqlTypedInsertMovementProtocol.TwoJson : SqlTypedInsertMovementProtocol.OneJson;
        await SqlRf3Protocol.EqualAsync(new DocumentResult(new(partition, SqlTypedInsertMovementProtocol.Table, id),
            SqlTypedInsertMovementProtocol.Revision, json, false, []),
            await PublicReadAssertions.DocumentAsync(healthy
                ? await root.GetAsync(new(partition, SqlTypedInsertMovementProtocol.Table, id), minimum.Token, token)
                : await root.GetAsync(new(partition, SqlTypedInsertMovementProtocol.Table, id), token)));
    }
    private static async Task PageAsync(List<QueryRow> expected, QueryPage actual)
    {
        await SqlRf3Protocol.EqualAsync(expected.ToArray(), actual.Rows.ToArray());
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.AccessPath).IsEqualTo(SqlTypedInsertMovementProtocol.FullScan);
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(NoCut);
    }
    private const long NoCut = 0;
}


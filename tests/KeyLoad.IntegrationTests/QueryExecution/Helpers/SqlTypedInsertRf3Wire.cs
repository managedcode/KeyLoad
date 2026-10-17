using Npgsql;
using NpgsqlTypes;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3Wire
{
    internal static async Task<CommitReceipt> ExecuteAsync(PostgresRf3Caller wire, SqlOperationRequest request, CancellationToken token)
    {
        await SqlCreateTableRf3Wire.ArmAsync(wire, request.CommandId ?? throw new InvalidOperationException(MissingIdentity), token);
        return request.Sql == SqlTypedInsertRf3Literal.Sql
            ? await wire.ExecuteAsync<CommitReceipt>(SqlTypedInsertRf3Literal.WireSql,
                [new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Boolean, Value = true }], prepared: true, token)
            : await wire.ExecuteAsync<CommitReceipt>(request.Sql, [], prepared: true, token);
    }
    internal static async Task RefuseAsync(PostgresRf3Caller wire, SqlOperationRequest request, string sqlState, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(wire, request, token))
            ?? throw new InvalidOperationException(MissingFailure);
        await Assert.That(error.SqlState).IsEqualTo(sqlState);
    }
    private const string MissingFailure = "The original native INSERT refusal was not retained.";
    private const string MissingIdentity = "The original SQL INSERT command identity is missing.";
}

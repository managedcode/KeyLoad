using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.UnitTests.ClientApi;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.QueryExecution;

/// <summary>AC-SQLC-005: actual SQL HTTP replies preserve conservative outcomes.</summary>
internal sealed class SqlClientOutcomeTests
{
    private const string ApiKey = "sql-client-outcome-test-key";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "orders";
    private const string Partition = "one";
    private const string JsonType = "application/json";
    private const string NullReply = "null";
    private const string SuccessReply = "{\"ok\":true}";
    private const string SuccessProperty = "ok";
    private const string Select = "SELECT * FROM orders";
    private const string CommentedSelect = "-- client\r\n/* outer /* inner */ */SELECT/* fields */ * FROM orders";
    private const string PlanCommentedSelect = "/* client */SELECT-- plan\n* FROM orders";
    private const string Execute = "EXEC keyload_query_capabilities @arguments";
    private const string CommentedExecute = "-- client\n/* nested /* inner */ */EXEC keyload_documents_commit @arguments";
    private const string Unknown = "INSERT INTO orders VALUES (@arguments)";
    private const string CteWrite = "WITH modified AS (SELECT * FROM orders) DELETE FROM modified";
    private const string Prefix = "SELECTED internal_data";
    private const string SelectInto = "SELECT * INTO archived_orders FROM orders";
    private const string QuotedRoot = "\"SELECT\" * FROM orders";
    private const string Unclosed = "/* unclosed EXEC keyload_documents_commit @arguments";
    private const string Empty = "-- only a comment";
    private const string ArgumentName = "arguments";
    private const string StableCommandId = "ed659b4c-1309-4dc4-9500-4c1ad8116a31";
    private const int TimeoutSeconds = 10;

    [Test]
    [Arguments(Select, ErrorCode.OwnershipLost)]
    [Arguments(CommentedSelect, ErrorCode.OwnershipLost)]
    [Arguments(PlanCommentedSelect, ErrorCode.OwnershipLost)]
    [Arguments(Execute, ErrorCode.UnknownWriteOutcome)]
    [Arguments(CommentedExecute, ErrorCode.UnknownWriteOutcome)]
    [Arguments(Unknown, ErrorCode.UnknownWriteOutcome)]
    [Arguments(CteWrite, ErrorCode.UnknownWriteOutcome)]
    [Arguments(Prefix, ErrorCode.UnknownWriteOutcome)]
    [Arguments(SelectInto, ErrorCode.UnknownWriteOutcome)]
    [Arguments(QuotedRoot, ErrorCode.UnknownWriteOutcome)]
    [Arguments(Unclosed, ErrorCode.UnknownWriteOutcome)]
    [Arguments(Empty, ErrorCode.UnknownWriteOutcome)]
    public async Task AC_SQLC_005_RealUnavailableReplyKeepsSqlAndClassifiesOutcome(string sql, ErrorCode expected)
        => await VerifyOutcomeAsync(sql, expected);

    [Test]
    [Arguments(CommentedSelect, ErrorCode.OwnershipLost)]
    [Arguments(CommentedExecute, ErrorCode.UnknownWriteOutcome)]
    [Arguments(CteWrite, ErrorCode.UnknownWriteOutcome)]
    public async Task AC_SQLC_005_ActualSocketAbortPreservesWriteOutcomes(string sql, ErrorCode expected)
        => await VerifyOutcomeAsync(sql, expected, abort: true);

    private static async Task VerifyOutcomeAsync(string sql, ErrorCode expected, bool abort = false)
    {
        SqlOperationRequest? received = null;
        var calls = 0;
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            var body = await JsonSerializer.DeserializeAsync<SqlOperationRequest>(context.Request.Body,
                JsonDefaults.Options, context.RequestAborted);
            received = body;
            context.Response.ContentType = JsonType;
            var first = Interlocked.Increment(ref calls) == 1;
            if (first && abort)
            {
                context.Abort();
                return;
            }
            context.Response.StatusCode = first ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;
            await context.Response.WriteAsync(first ? NullReply : SuccessReply, context.RequestAborted);
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        var client = new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution());
        var partition = new PartitionRef(Tenant, Database, Domain, Partition);
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [ArgumentName] = JsonSerializer.SerializeToElement(
                new CommandRequest(Guid.Parse(StableCommandId), partition, []), JsonDefaults.Options)
        };
        var request = new SqlOperationRequest(partition, sql, parameters);
        var result = await client.ExecuteSqlAsync(request, cancellation.Token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(JsonDefaults.Serialize(received).AsSpan().SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        var next = await client.ExecuteSqlAsync(request, cancellation.Token);
        await Assert.That(next.IsSuccess).IsTrue();
        await Assert.That(next.Value.GetProperty(SuccessProperty).GetBoolean()).IsTrue();
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(JsonDefaults.Serialize(received).AsSpan().SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
    }
}

using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.UnitTests.Features.ClientApi;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.QueryExecution;

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
    private const string Explain = "/* client */EXPLAIN-- plan\nSELECT * FROM orders";
    private const string Call = "CALL keyload_query_capabilities(@arguments)";
    private const string CommentedCall = "-- client\n/* nested /* inner */ */CALL keyload_documents_put(@arguments)";
    private const string Unknown = "INSERT INTO orders VALUES (@arguments)";
    private const string With = "WITH modified AS (DELETE FROM orders RETURNING *) SELECT * FROM modified";
    private const string Prefix = "SELECTED internal_data";
    private const string ExplainWrite = "EXPLAIN ANALYZE DELETE FROM orders";
    private const string QuotedRoot = "\"SELECT\" * FROM orders";
    private const string Unclosed = "/* unclosed CALL keyload_documents_put(@arguments)";
    private const string Empty = "-- only a comment";
    private const string OpenComment = "/*";
    private const string CloseComment = "*/";
    private const string ArgumentName = "arguments";
    private const string StableCommandId = "ed659b4c-1309-4dc4-9500-4c1ad8116a31";
    private const char Padding = 'x';
    private const char MultibytePadding = '\u00E9';
    private const int TimeoutSeconds = 10;

    [Test]
    [Arguments(Select, ErrorCode.OwnershipLost)]
    [Arguments(CommentedSelect, ErrorCode.OwnershipLost)]
    [Arguments(Explain, ErrorCode.OwnershipLost)]
    [Arguments(Call, ErrorCode.UnknownWriteOutcome)]
    [Arguments(CommentedCall, ErrorCode.UnknownWriteOutcome)]
    [Arguments(Unknown, ErrorCode.UnknownWriteOutcome)]
    [Arguments(With, ErrorCode.UnknownWriteOutcome)]
    [Arguments(Prefix, ErrorCode.UnknownWriteOutcome)]
    [Arguments(ExplainWrite, ErrorCode.UnknownWriteOutcome)]
    [Arguments(QuotedRoot, ErrorCode.UnknownWriteOutcome)]
    [Arguments(Unclosed, ErrorCode.UnknownWriteOutcome)]
    [Arguments(Empty, ErrorCode.UnknownWriteOutcome)]
    public async Task AC_SQLC_005_RealUnavailableReplyKeepsSqlAndClassifiesOutcome(string sql, ErrorCode expected)
        => await VerifyOutcomeAsync(sql, expected);

    [Test]
    public async Task AC_SQLC_005_ClassificationBoundsRemainConservative()
    {
        var limits = new DatabaseLimits();
        await VerifyOutcomeAsync(OpenComment + new string(Padding, limits.MaxQueryBytes) + CloseComment + Select,
            ErrorCode.UnknownWriteOutcome);
        await VerifyOutcomeAsync(OpenComment + new string(MultibytePadding, limits.MaxQueryBytes / 2) + CloseComment + Select,
            ErrorCode.UnknownWriteOutcome);
        var deeplyNested = string.Concat(Enumerable.Repeat(OpenComment, limits.MaxQueryDepth + 1))
            + string.Concat(Enumerable.Repeat(CloseComment, limits.MaxQueryDepth + 1)) + Select;
        await VerifyOutcomeAsync(deeplyNested, ErrorCode.UnknownWriteOutcome);
    }

    [Test]
    [Arguments(CommentedSelect, ErrorCode.OwnershipLost)]
    [Arguments(CommentedCall, ErrorCode.UnknownWriteOutcome)]
    [Arguments(With, ErrorCode.UnknownWriteOutcome)]
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
            { context.Abort(); return; }
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

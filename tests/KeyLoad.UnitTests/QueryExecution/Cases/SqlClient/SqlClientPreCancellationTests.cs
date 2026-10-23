using KeyLoad.Client;
using KeyLoad.UnitTests.Features.ClientApi;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-SQLC-004/005: original SDK cancellation outcomes stay bounded.</summary>
internal sealed class SqlClientPreCancellationTests
{
    private const string Select = "SELECT * FROM orders";
    private const string SelectKeyword = "SELECT";
    private const string CommentedSelect = "SELECT/* read */ * FROM orders";
    private const string Execute = "EXEC keyload_documents_commit @arguments";
    private const string Open = "/*";
    private const string Close = "*/";
    private const string ApiKey = "sql-cancel-test-key";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "orders";
    private const string Partition = "one";
    private const string SuccessReply = "{\"ok\":true}";
    private const int HealthyTimeoutSeconds = 10;
    private const char Padding = 'x';
    private const char Whitespace = ' ';
    private const int GapCharacters = 200;
    private const int OriginalInspectionCharacters = 256;

    [Test]
    public async Task AC_SQLC_005_PreCancelledKnownReadsKeepOriginalSdkOutcomes()
    {
        await VerifyAsync(Select, ErrorCode.Cancelled);
        await VerifyAsync(CommentedSelect, ErrorCode.Cancelled);
        await VerifyAsync(Execute, ErrorCode.UnknownWriteOutcome);
        var longPrefix = Open + new string(Padding, OriginalInspectionCharacters) + Close + Select;
        await VerifyAsync(longPrefix, ErrorCode.UnknownWriteOutcome);
        var gap = new string(Whitespace, GapCharacters);
        await VerifyAsync(gap + gap + Select, ErrorCode.UnknownWriteOutcome);
        var edge = new string(Whitespace, OriginalInspectionCharacters - SelectKeyword.Length);
        await VerifyAsync(edge + Select, ErrorCode.UnknownWriteOutcome);
        await VerifyAsync(edge[1..] + Select, ErrorCode.Cancelled);
    }

    private static async Task VerifyAsync(string sql, ErrorCode expected)
    {
        var calls = 0;
        SqlOperationRequest? received = null;
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            received = await global::System.Text.Json.JsonSerializer.DeserializeAsync<SqlOperationRequest>(
                context.Request.Body, JsonDefaults.Options, context.RequestAborted);
            Interlocked.Increment(ref calls);
            await context.Response.WriteAsync(SuccessReply, context.RequestAborted);
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var request = new SqlOperationRequest(new(Tenant, Database, Domain, Partition), sql);
        // The original 256-character inspection window is the SDK scan bound for these outcomes.
        var client = new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution(
            new KeyLoadClientExecutionOptions { MaximumSqlInspectionBytes = OriginalInspectionCharacters }));
        var result = await client.ExecuteSqlAsync(request, cancellation.Token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(received).IsNull();
        using var healthy = new CancellationTokenSource(TimeSpan.FromSeconds(HealthyTimeoutSeconds), TimeProvider.System);
        var next = await client.ExecuteSqlAsync(request, healthy.Token);
        await Assert.That(next.IsSuccess).IsTrue();
        await Assert.That(next.Value.GetRawText()).IsEqualTo(SuccessReply);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(JsonDefaults.Serialize(received).AsSpan().SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
    }
}

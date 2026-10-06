using KeyLoad.Client;
using KeyLoad.UnitTests.Features.ClientApi;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-SQLC-004/005: original SDK cancellation outcomes stay bounded.</summary>
internal sealed class SqlClientPreCancellationTests
{
    private const string Select = "SELECT * FROM orders";
    private const string SelectKeyword = "SELECT";
    private const string Explain = "EXPLAIN SELECT * FROM orders";
    private const string ExplainKeyword = "EXPLAIN";
    private const string Call = "CALL keyload_documents_commit(@arguments)";
    private const string Open = "/*";
    private const string Close = "*/";
    private const string ApiKey = "sql-cancel-test-key";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "orders";
    private const string Partition = "one";
    private const string SuccessReply = "{}";
    private const char Padding = 'x';
    private const char Whitespace = ' ';
    private const int GapCharacters = 200;
    private const int OriginalInspectionCharacters = 256;

    [Test]
    public async Task AC_SQLC_005_PreCancelledKnownReadsKeepOriginalSdkOutcomes()
    {
        await VerifyAsync(Select, ErrorCode.Cancelled);
        await VerifyAsync(Explain, ErrorCode.Cancelled);
        await VerifyAsync(Call, ErrorCode.UnknownWriteOutcome);
        var longPrefix = Open + new string(Padding, OriginalInspectionCharacters) + Close + Select;
        await VerifyAsync(longPrefix, ErrorCode.UnknownWriteOutcome);
        var gap = new string(Whitespace, GapCharacters);
        await VerifyAsync(gap + ExplainKeyword + gap + Select, ErrorCode.UnknownWriteOutcome);
        var edge = new string(Whitespace, OriginalInspectionCharacters - SelectKeyword.Length);
        await VerifyAsync(edge + Select, ErrorCode.UnknownWriteOutcome);
        await VerifyAsync(edge[1..] + Select, ErrorCode.Cancelled);
    }

    private static async Task VerifyAsync(string sql, ErrorCode expected)
    {
        var calls = 0;
        await using var server = await KeyLoadClientKestrelServer.StartAsync(context =>
        {
            Interlocked.Increment(ref calls);
            return context.Response.WriteAsync(SuccessReply, context.RequestAborted);
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var request = new SqlOperationRequest(new(Tenant, Database, Domain, Partition), sql);
        var result = await new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution()).ExecuteSqlAsync(request, cancellation.Token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(calls).IsEqualTo(0);
    }
}

using System.Text;
using System.Text.Json;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-AISQL-007: reject byte/structure overages and cancellation before canonical decoding.</summary>
internal sealed class SqlOperationBudgetTests
{
    private const string UnicodeComment = " é";
    private const int ExtraParameterCount = 257;
    private const string ParameterPrefix = "p";
    private const string NestedObject = "{\"nested\":{\"inner\":{}}}";
    private const string NestedArray = "[0,1,2,3,4]";

    [Test]
    public async Task AcAiSql007TextBudgetCountsUtf8BytesAndAcceptsTheExactBoundary()
    {
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        var bytes = Encoding.UTF8.GetByteCount(request.Sql);
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(request,
                SqlOperationTestData.Limits with { MaxQueryBytes = bytes }),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        await SqlOperationTestData.Reject(request, ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with { MaxQueryBytes = bytes - 1 });
        var unicode = new SqlOperationRequest(request.Partition, SqlOperationTestData.Select + UnicodeComment);
        await SqlOperationTestData.Reject(unicode, ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with { MaxQueryBytes = unicode.Sql.Length });
    }

    [Test]
    public async Task AcAiSql007CompleteEnvelopeBudgetHasAnInclusiveByteBoundary()
    {
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        var bytes = JsonDefaults.Serialize(request).Length;
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(request, maximumPayloadBytes: bytes),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        await SqlOperationTestData.Reject(request, ErrorCode.ResourceExhausted, maximumPayloadBytes: bytes - 1);
    }

    [Test]
    public async Task AcAiSql007ParameterCountDepthAndTokenBudgetsBoundCallArgumentWork()
    {
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        var parameters = Enumerable.Range(0, ExtraParameterCount).ToDictionary(index =>
            ParameterPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => JsonSerializer.SerializeToElement(0), StringComparer.Ordinal);
        await SqlOperationTestData.Reject(request with { Parameters = parameters }, ErrorCode.BudgetExceeded);
        await SqlOperationTestData.Reject(SqlOperationTestData.Request(request.Sql,
                JsonSerializer.Deserialize<JsonElement>(NestedObject)), ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with { MaxJsonDepth = 2 });
        await SqlOperationTestData.Reject(SqlOperationTestData.Request(request.Sql,
                JsonSerializer.Deserialize<JsonElement>(NestedArray)), ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with { MaxQueryTokens = 2 });
        await SqlOperationTestData.Reject(request, ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with { MaxQueryTokens = 1 });
    }

    [Test]
    public async Task AcAiSql007CancellationWinsBeforeMalformedInputOrPayloadAllocation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities) with { Sql = null! };
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            SqlOperationTestData.Compile(request, cancellationToken: cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }
}

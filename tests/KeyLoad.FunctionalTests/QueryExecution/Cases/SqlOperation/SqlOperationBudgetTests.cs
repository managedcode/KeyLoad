using System.Text.Json;
using KeyLoad.FunctionalTests.ClientApi;

namespace KeyLoad.FunctionalTests.QueryExecution;

/// <summary>AC-AISQL-007: reject byte/structure overages and cancellation before canonical decoding.</summary>
internal sealed class SqlOperationBudgetTests
{
    private const int ExtraParameterCount = 257;
    private const string ParameterPrefix = "p";

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
    public async Task AcAiSql007ParameterCountBudgetBoundsCallArgumentWork()
    {
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        var parameters = Enumerable.Range(0, ExtraParameterCount).ToDictionary(index =>
            ParameterPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => JsonSerializer.SerializeToElement(0), StringComparer.Ordinal);
        await SqlOperationTestData.Reject(request with
        {
            Parameters = parameters
        }, ErrorCode.BudgetExceeded);
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(request),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
    }

    [Test]
    public async Task AcAiSql007CancellationWinsBeforeMalformedInputOrPayloadAllocation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities) with
        {
            Sql = null!
        };
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            SqlOperationTestData.Compile(request, cancellationToken: cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }
}

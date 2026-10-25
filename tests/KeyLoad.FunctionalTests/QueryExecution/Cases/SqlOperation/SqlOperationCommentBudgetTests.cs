using KeyLoad.FunctionalTests.ClientApi;

namespace KeyLoad.FunctionalTests.QueryExecution;

/// <summary>AC-SQLC-004: original comment bytes and cancellation remain independent.</summary>
internal sealed class SqlOperationCommentBudgetTests
{
    [Test]
    public async Task AcSqlc004OriginalCompleteEnvelopeIncludesCommentsAtTheInclusiveBoundary()
    {
        var request = SqlOperationCommentTestData.Request(SqlOperationCommentTestData.Block
            + SqlOperationCommentTestData.PlainCall + SqlOperationCommentTestData.EndOfFileLine);
        var bytes = JsonDefaults.Serialize(request).Length;
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(request, maximumPayloadBytes: bytes),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        await SqlOperationTestData.Reject(request, ErrorCode.ResourceExhausted,
            maximumPayloadBytes: bytes - SqlOperationCommentTestData.BoundaryStep);
    }

    [Test]
    public async Task AcSqlc004CallerCancellationWinsBeforeLongMalformedCommentOrCanonicalDecode()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var sql = SqlOperationCommentTestData.OpenBlock + new string(SqlOperationCommentTestData.Padding,
            SqlOperationCommentTestData.ChunkCharacters * SqlOperationCommentTestData.LongCommentChunks);
        var request = SqlOperationCommentTestData.Request(sql);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            SqlOperationTestData.Compile(request, cancellationToken: cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task AcSqlc004SyntaxTriviaUsesTheOriginalCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(cancellationToken: cancellation.Token);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(SqlOperationCommentTestData.Block, budget.Check, cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }
}

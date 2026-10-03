using System.Text;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-SQLC-004: original comment bytes, token bounds and cancellation remain independent.</summary>
internal sealed class SqlOperationCommentBudgetTests
{
    [Test]
    public async Task AcSqlc004Utf8CommentBytesCountAtTheExactTextBoundary()
    {
        var request = SqlOperationCommentTestData.Request(SqlOperationCommentTestData.UnicodeBlock
            + SqlOperationCommentTestData.PlainCall);
        var bytes = Encoding.UTF8.GetByteCount(request.Sql);
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(request,
                SqlOperationTestData.Limits with { MaxQueryBytes = bytes }),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        await SqlOperationTestData.Reject(request, ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with { MaxQueryBytes = bytes - SqlOperationCommentTestData.BoundaryStep });
        await SqlOperationTestData.Reject(request, ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with { MaxQueryBytes = request.Sql.Length });
    }

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
    public async Task AcSqlc004CommentsConsumeNoTokensButTheTerminatorStillDoes()
    {
        var request = SqlOperationCommentTestData.Request(SqlOperationCommentTestData.Block
            + SqlOperationCommentTestData.PlainCall + SqlOperationCommentTestData.Block);
        var limits = SqlOperationTestData.Limits with { MaxQueryTokens = SqlOperationCommentTestData.CallTokens };
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(request, limits),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        await SqlOperationTestData.Reject(request, ErrorCode.BudgetExceeded,
            limits with { MaxQueryTokens = SqlOperationCommentTestData.CallTokens - SqlOperationCommentTestData.BoundaryStep });
        await SqlOperationTestData.Reject(request with { Sql = request.Sql + SqlOperationCommentTestData.Semicolon },
            ErrorCode.BudgetExceeded, limits);
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
        var reader = new SqlOperationSyntaxReader(SqlOperationCommentTestData.Block,
            SqlOperationCommentTestData.CallTokens, SqlOperationCommentTestData.ShallowDepth, cancellation.Token);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(reader.SkipTrivia);
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }
}

using System.Text;
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
                SqlOperationTestData.Limits with
                {
                    MaxQueryBytes = bytes
                }),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        await SqlOperationTestData.Reject(request, ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with
            {
                MaxQueryBytes = bytes - SqlOperationCommentTestData.BoundaryStep
            });
        await SqlOperationTestData.Reject(request, ErrorCode.BudgetExceeded,
            SqlOperationTestData.Limits with
            {
                MaxQueryBytes = request.Sql.Length
            });
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
        var limits = SqlOperationTestData.Limits with
        {
            MaxQueryTokens = SqlOperationCommentTestData.CallTokens
        };
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(SqlOperationTestData.Limits));
        using var reader = new global::KeyLoad.Features.QueryExecution.ScriptDomBoundedReader(request.Sql, budget.Check);
        var parser = new global::Microsoft.SqlServer.TransactSql.ScriptDom.TSql180Parser(true,
            global::Microsoft.SqlServer.TransactSql.ScriptDom.SqlEngineType.Standalone);
        var tokens = parser.GetTokenStream(reader, out var errors);
        await Assert.That(errors.Count).IsEqualTo(0);
        global::KeyLoad.Features.QueryExecution.ScriptDomTokenBounds.Require(tokens, limits, budget.Check);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            global::KeyLoad.Features.QueryExecution.ScriptDomTokenBounds.Require(tokens,
                limits with
                {
                    MaxQueryTokens = SqlOperationCommentTestData.CallTokens - SqlOperationCommentTestData.BoundaryStep
                }, budget.Check)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        using var terminated = new global::KeyLoad.Features.QueryExecution.ScriptDomBoundedReader(
            request.Sql + SqlOperationCommentTestData.Semicolon, budget.Check);
        var terminatedTokens = parser.GetTokenStream(terminated, out var terminatedErrors);
        await Assert.That(terminatedErrors.Count).IsEqualTo(0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            global::KeyLoad.Features.QueryExecution.ScriptDomTokenBounds.Require(terminatedTokens, limits, budget.Check)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(request),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
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
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(SqlOperationTestData.Limits),
            cancellationToken: cancellation.Token);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.Parse(SqlOperationCommentTestData.Block,
                UnitExecutionOptions.DatabaseLimits(SqlOperationTestData.Limits), budget.Check, cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }
}

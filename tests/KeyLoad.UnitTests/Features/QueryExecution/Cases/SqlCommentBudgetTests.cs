using KeyLoad.Core;
using KeyLoad.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlCommentBudgetTests
{
    [Test]
    public async Task AcSqlc004_CancellationIsObservedBeforeTheNextTriviaChunk()
    {
        using var cancellation = new CancellationTokenSource();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()), cancellationToken: cancellation.Token);
        var sql = SqlCommentTestData.LongCommentPrefix
            + new string(SqlCommentTestData.LongCommentCharacter, SqlCommentTestData.LongCommentSize)
            + SqlCommentTestData.LongCommentSuffix;
        var offset = 0;
        var state = default(SqlTriviaState);

        var status = SqlTriviaReader.Read(sql, ref offset, ref state, SqlCommentTestData.MaximumCommentDepth);
        await Assert.That(status).IsEqualTo(SqlTriviaStatus.More);
        await Assert.That(offset).IsGreaterThan(0);
        await Assert.That(offset).IsLessThanOrEqualTo(SqlTriviaReader.MaximumChunkCharacters);
        await cancellation.CancelAsync();
        var error = Assert.ThrowsExactly<OperationCanceledException>(budget.Check);
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
    }
}

using KeyLoad.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-SQLC-002/004: trivia boundaries and bounded scalar progress.</summary>
internal sealed class SqlTriviaReaderTests
{
    private const string Nested = " \r\n-- ignored ; CALL\r\n/* outer /* inner */ tail */SELECT";
    private const string Select = "SELECT";
    private const string Unclosed = "/* unfinished";
    private const string LineAtEnd = "-- unfinished line";
    private const string Deep = "/* /* */ */SELECT";
    private const string Quoted = "'/* -- */'";
    private const string Gap = "SEL/* gap */ECT";
    private const int DepthLimit = 2;
    private const int ShallowDepth = 1;
    private const int LongCommentLength = 4_096;
    private const char CommentCharacter = 'x';
    private const string OpenComment = "/*";
    private const string CloseComment = "*/";
    private const char Whitespace = ' ';

    [Test]
    public async Task AC_SQLC_002_CommentsStopAtTheOriginalFirstToken()
    {
        var (status, offset) = Drain(Nested, DepthLimit);
        await Assert.That(status).IsEqualTo(SqlTriviaStatus.Complete);
        await Assert.That(Nested[offset..]).IsEqualTo(Select);
        var (_, quotedOffset) = Drain(Quoted, DepthLimit);
        var (_, identifierOffset) = Drain(Gap, DepthLimit);
        await Assert.That(quotedOffset).IsEqualTo(0);
        await Assert.That(identifierOffset).IsEqualTo(0);
    }

    [Test]
    public async Task AC_SQLC_002_UnclosedBlocksAndNestedDepthAreDistinct()
    {
        await Assert.That(Drain(Unclosed, DepthLimit).Status).IsEqualTo(SqlTriviaStatus.UnterminatedComment);
        await Assert.That(Drain(LineAtEnd, DepthLimit).Status).IsEqualTo(SqlTriviaStatus.Complete);
        await Assert.That(Drain(Deep, ShallowDepth).Status).IsEqualTo(SqlTriviaStatus.DepthLimitExceeded);
        var (status, offset) = Drain(Deep, DepthLimit);
        await Assert.That(status).IsEqualTo(SqlTriviaStatus.Complete);
        await Assert.That(Deep[offset..]).IsEqualTo(Select);
    }

    [Test]
    public async Task AC_SQLC_004_EveryChunkHasBoundedPositiveProgress()
    {
        var sql = OpenComment + new string(CommentCharacter, LongCommentLength) + CloseComment + Select;
        var state = new SqlTriviaState();
        var offset = 0;
        SqlTriviaStatus status;
        do
        {
            var before = offset;
            status = SqlTriviaReader.Read(sql, ref offset, ref state, DepthLimit);
            await Assert.That(offset - before).IsLessThanOrEqualTo(SqlTriviaReader.MaximumChunkCharacters);
            if (status == SqlTriviaStatus.More)
            {
                await Assert.That(offset).IsGreaterThan(before);
            }
        } while (status == SqlTriviaStatus.More);
        await Assert.That(status).IsEqualTo(SqlTriviaStatus.Complete);
        await Assert.That(sql[offset..]).IsEqualTo(Select);
    }

    [Test]
    public async Task AC_SQLC_004_CommentDelimitersAcrossChunkBoundariesRemainTrivia()
    {
        var prefix = new string(Whitespace, SqlTriviaReader.MaximumChunkCharacters - 1);
        var sql = prefix + OpenComment + new string(CommentCharacter, LongCommentLength) + CloseComment + Select;
        var (status, offset) = Drain(sql, DepthLimit);
        await Assert.That(status).IsEqualTo(SqlTriviaStatus.Complete);
        await Assert.That(sql[offset..]).IsEqualTo(Select);
    }

    private static (SqlTriviaStatus Status, int Offset) Drain(string sql, int maximumDepth)
    {
        var state = new SqlTriviaState();
        var offset = 0;
        SqlTriviaStatus status;
        do
        {
            status = SqlTriviaReader.Read(sql, ref offset, ref state, maximumDepth);
        } while (status == SqlTriviaStatus.More);
        return (status, offset);
    }
}

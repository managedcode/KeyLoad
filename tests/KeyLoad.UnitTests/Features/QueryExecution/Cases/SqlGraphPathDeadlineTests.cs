using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphPathDeadlineTests
{
    private const int DeadlineSeconds = 1;
    private const string UnterminatedPrefix = "SELECT * FROM GRAPH_SHORTEST_PATH('";
    private const char Padding = 'x';
    private const int PaddingLength = 8_192;
    private const string Principal = SqlGraphPathTestSupport.Principal;

    [Test]
    public async Task SharedDeadlineInterruptsBoundedSqlParsingBeforeSyntaxFailure()
    {
        using var database = new TestDatabase(new() { QueryDeadlineSeconds = DeadlineSeconds });
        var engine = new QueryEngine(database.Database);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPathSql(Principal,
            SqlGraphPathTestSupport.Request(database, UnterminatedPrefix + new string(Padding, PaddingLength)),
            new ExpireDuringParsingTimeProvider()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private sealed class ExpireDuringParsingTimeProvider : TimeProvider
    {
        private const int ExpireOnTimestampCall = 20;
        private const long ExpiredTimestamp = 2 * TimeSpan.TicksPerSecond;
        private int timestampCalls;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
            => Interlocked.Increment(ref timestampCalls) >= ExpireOnTimestampCall ? ExpiredTimestamp : 0;
    }
}

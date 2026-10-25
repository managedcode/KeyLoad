namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class IndexedReferenceCursorClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

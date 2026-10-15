namespace KeyLoad.UnitTests.Features.QueryExecution;

internal readonly struct QueryEquivalentApplicationNumber
{
    private const string UnexpectedOperator = "The application conversion operator must not execute during query lowering.";
    private static int calls;
    internal static int Calls => Volatile.Read(ref calls);
    public static implicit operator decimal(QueryEquivalentApplicationNumber value)
    {
        _ = value;
        Interlocked.Increment(ref calls);
        throw new InvalidOperationException(UnexpectedOperator);
    }
}

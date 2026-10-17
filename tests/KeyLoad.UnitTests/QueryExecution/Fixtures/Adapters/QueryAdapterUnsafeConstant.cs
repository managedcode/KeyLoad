namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryAdapterUnsafeConstant
{
    public int Calls;
    public string Value
    {
        get
        {
            Calls++;
            throw new InvalidOperationException(QueryAdapterTestTokens.UnsafeGetterMessage);
        }
    }
}

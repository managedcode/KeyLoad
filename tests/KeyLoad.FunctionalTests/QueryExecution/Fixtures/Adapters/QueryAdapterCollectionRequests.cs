using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryAdapterCollectionRequests
{
    internal const string Sql = "SELECT TOP (10) * FROM orders WHERE status = 'open' ORDER BY id";
    internal static KeyLoadQuery<QueryAdapterCollectionDocument> Base(PartitionRef partition)
        => KeyLoadQuery.From<QueryAdapterCollectionDocument>(partition, QueryAdapterCollectionFixture.Collection,
            UnitClientOptions.Translation()).OrderBy(row => QueryFunctions.DocumentId(row)).Take(10);
    internal static AstQueryRequest Json(PartitionRef partition, bool array)
        => QueryAdapterTestSupport.RoundTrip(new(partition,
            new(QueryAdapterCollectionFixture.Collection, null, [new("*", "*")],
                new Comparison(new FieldOperand(array ? "/slots/0" : "/status"), "=",
                    array ? ValueOperand.Create(2m) : ValueOperand.Create("open")), [new("/@id", false)], 10),
            AllowFullScan: true));
    internal static Action[] Refusals(PartitionRef partition, QueryAdapterUnsafeConstant constant)
    {
        var original = KeyLoadQuery.From<QueryAdapterOrder>(partition, QueryAdapterCollectionFixture.Collection,
            UnitClientOptions.Translation());
        var collection = Base(partition);
        return [() => original.Where(row => row.Status == constant.Value),
            () => original.Where(row => row.Status.StartsWith('o')),
            () => original.Where(row => (int)row.Number > 2),
            () => collection.Where(row => row.Values.Count > 1),
            () => collection.Where(row => row.Fixed.Length > 1),
            () => collection.Where(row => row.Fixed.IsEmpty),
            () => collection.OrderBy(row => row.Fixed.Length),
            () => collection.Select(row => row.Values.Count)];
    }
}

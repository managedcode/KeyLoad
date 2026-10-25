using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryAdapterTestSupport
{
    internal static AstQueryRequest RoundTrip(AstQueryRequest request)
        => JsonDefaults.Deserialize<AstQueryRequest>(JsonDefaults.Serialize(request));

    internal static async Task SameRows(QueryPage expected, QueryPage actual)
        => await Assert.That(JsonDefaults.Serialize(actual.Rows).SequenceEqual(JsonDefaults.Serialize(expected.Rows))).IsTrue();
}

using System.Text.Json.Serialization;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterTestTokens
{
    internal const string Status = "status";
    internal const string StatusPath = "/status";
    internal const string OddName = "odd.name";
    internal const string OptionalNumber = "n";
    internal const string Partition = "partition";
    internal const string BadParameter = "bad";
    internal const string Open = "open";
    internal const string Hold = "hold";
    internal const string Closed = "closed";
    internal const string UnsafeGetterMessage = "Application code must not execute.";
}

internal sealed record QueryAdapterOrder(decimal Number, string Status)
{
    [JsonPropertyName(QueryAdapterTestTokens.OddName)]
    public string? Secret { get; init; }

    [JsonPropertyName(QueryAdapterTestTokens.OptionalNumber)]
    public decimal? Optional { get; init; }
}

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

internal static class QueryAdapterTestSupport
{
    internal static AstQueryRequest RoundTrip(AstQueryRequest request)
        => JsonDefaults.Deserialize<AstQueryRequest>(JsonDefaults.Serialize(request));

    internal static async Task SameRows(QueryPage expected, QueryPage actual)
        => await Assert.That(JsonDefaults.Serialize(actual.Rows)).IsEqualTo(JsonDefaults.Serialize(expected.Rows));
}

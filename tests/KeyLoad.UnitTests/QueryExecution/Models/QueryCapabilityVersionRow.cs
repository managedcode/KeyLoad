using System.Text.Json.Serialization;

namespace KeyLoad.UnitTests.QueryExecution;

internal readonly record struct QueryCapabilityVersionRow([property: JsonPropertyName(QueryCapabilityVersionRow.OrderIdWireName)] string OrderId,
    [property: JsonPropertyName(QueryCapabilityVersionRow.TitleWireName)] string Title, [property: JsonPropertyName(QueryCapabilityVersionRow.ScoreWireName)] decimal Score,
    [property: JsonPropertyName(QueryCapabilityVersionRow.ActiveWireName)] bool Active)
{
    private const string OrderIdWireName = "order_id";
    private const string TitleWireName = "title";
    private const string ScoreWireName = "score";
    private const string ActiveWireName = "active";
}

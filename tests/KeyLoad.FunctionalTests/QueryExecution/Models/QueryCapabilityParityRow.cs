using System.Text.Json.Serialization;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal readonly record struct QueryCapabilityParityRow([property: JsonPropertyName(QueryCapabilityParityRow.OrderIdWireName)] string OrderId,
    [property: JsonPropertyName(QueryCapabilityParityRow.TitleWireName)] string Title, [property: JsonPropertyName(QueryCapabilityParityRow.ScoreWireName)] decimal Score,
    [property: JsonPropertyName(QueryCapabilityParityRow.ActiveWireName)] bool Active)
{
    private const string OrderIdWireName = "order_id";
    private const string TitleWireName = "title";
    private const string ScoreWireName = "score";
    private const string ActiveWireName = "active";
}

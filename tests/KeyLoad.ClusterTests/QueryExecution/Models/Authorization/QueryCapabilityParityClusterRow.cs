using System.Text.Json.Serialization;

namespace KeyLoad.ClusterTests.QueryExecution;

internal readonly record struct QueryCapabilityParityClusterRow([property: JsonPropertyName(QueryCapabilityParityClusterRow.OrderIdWireName)] string OrderId,
    [property: JsonPropertyName(QueryCapabilityParityClusterRow.TitleWireName)] string Title, [property: JsonPropertyName(QueryCapabilityParityClusterRow.ScoreWireName)] decimal Score,
    [property: JsonPropertyName(QueryCapabilityParityClusterRow.ActiveWireName)] bool Active)
{
    private const string OrderIdWireName = "order_id";
    private const string TitleWireName = "title";
    private const string ScoreWireName = "score";
    private const string ActiveWireName = "active";
}

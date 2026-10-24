using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal readonly record struct QueryCapabilityVersionClusterRow([property: JsonPropertyName(QueryCapabilityVersionClusterRow.OrderIdWireName)] string OrderId,
    [property: JsonPropertyName(QueryCapabilityVersionClusterRow.TitleWireName)] string Title, [property: JsonPropertyName(QueryCapabilityVersionClusterRow.ScoreWireName)] decimal Score,
    [property: JsonPropertyName(QueryCapabilityVersionClusterRow.ActiveWireName)] bool Active)
{
    private const string OrderIdWireName = "order_id";
    private const string TitleWireName = "title";
    private const string ScoreWireName = "score";
    private const string ActiveWireName = "active";
}

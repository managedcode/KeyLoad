using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal readonly record struct QueryCapabilityVersionRf3Row([property: JsonPropertyName(QueryCapabilityVersionRf3Row.OrderIdWireName)] string OrderId,
    [property: JsonPropertyName(QueryCapabilityVersionRf3Row.TitleWireName)] string Title, [property: JsonPropertyName(QueryCapabilityVersionRf3Row.ScoreWireName)] decimal Score,
    [property: JsonPropertyName(QueryCapabilityVersionRf3Row.ActiveWireName)] bool Active)
{
    private const string OrderIdWireName = "order_id";
    private const string TitleWireName = "title";
    private const string ScoreWireName = "score";
    private const string ActiveWireName = "active";
}

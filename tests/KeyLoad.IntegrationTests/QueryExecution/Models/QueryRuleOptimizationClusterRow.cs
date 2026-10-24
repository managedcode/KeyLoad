using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal readonly record struct QueryRuleOptimizationClusterRow(
    [property: JsonPropertyName(QueryRuleOptimizationClusterRow.StatusWireName)] string Status,
    [property: JsonPropertyName(QueryRuleOptimizationClusterRow.RankWireName)] decimal Rank,
    [property: JsonPropertyName(QueryRuleOptimizationClusterRow.NestedWireName)] QueryRuleOptimizationClusterNested Nested)
{
    private const string StatusWireName = "status";
    private const string RankWireName = "rank";
    private const string NestedWireName = "nested";
}

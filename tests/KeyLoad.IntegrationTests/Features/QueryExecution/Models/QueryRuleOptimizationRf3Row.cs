using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal readonly record struct QueryRuleOptimizationRf3Row(
    [property: JsonPropertyName(QueryRuleOptimizationRf3Row.StatusWireName)] string Status,
    [property: JsonPropertyName(QueryRuleOptimizationRf3Row.RankWireName)] decimal Rank,
    [property: JsonPropertyName(QueryRuleOptimizationRf3Row.NestedWireName)] QueryRuleOptimizationRf3Nested Nested)
{
    private const string StatusWireName = "status";
    private const string RankWireName = "rank";
    private const string NestedWireName = "nested";
}

internal readonly record struct QueryRuleOptimizationRf3Nested(
    [property: JsonPropertyName(QueryRuleOptimizationRf3Nested.RevisionWireName)] decimal Revision)
{
    private const string RevisionWireName = "@revision";
}

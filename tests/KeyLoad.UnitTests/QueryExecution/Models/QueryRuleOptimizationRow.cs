using System.Text.Json.Serialization;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal readonly record struct QueryRuleOptimizationRow(
    [property: JsonPropertyName(QueryRuleOptimizationRow.StatusWireName)] string Status,
    [property: JsonPropertyName(QueryRuleOptimizationRow.RankWireName)] decimal Rank,
    [property: JsonPropertyName(QueryRuleOptimizationRow.NestedWireName)] QueryRuleOptimizationNested Nested)
{
    private const string StatusWireName = "status";
    private const string RankWireName = "rank";
    private const string NestedWireName = "nested";
}

internal readonly record struct QueryRuleOptimizationNested(
    [property: JsonPropertyName(QueryRuleOptimizationNested.RevisionWireName)] decimal Revision)
{
    private const string RevisionWireName = "@revision";
}

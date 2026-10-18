using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal readonly record struct QueryRuleOptimizationClusterNested(
    [property: JsonPropertyName(QueryRuleOptimizationClusterNested.RevisionWireName)] decimal Revision)
{
    private const string RevisionWireName = "@revision";
}

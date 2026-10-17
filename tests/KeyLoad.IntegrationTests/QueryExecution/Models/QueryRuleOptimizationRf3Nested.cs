using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal readonly record struct QueryRuleOptimizationRf3Nested(
    [property: JsonPropertyName(QueryRuleOptimizationRf3Nested.RevisionWireName)] decimal Revision)
{
    private const string RevisionWireName = "@revision";
}

using System.Text.Json.Serialization;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal readonly record struct QueryRuleOptimizationNested(
    [property: JsonPropertyName(QueryRuleOptimizationNested.RevisionWireName)] decimal Revision)
{
    private const string RevisionWireName = "@revision";
}

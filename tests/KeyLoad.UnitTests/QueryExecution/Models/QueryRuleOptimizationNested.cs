using System.Text.Json.Serialization;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal readonly record struct QueryRuleOptimizationNested(
    [property: JsonPropertyName(QueryRuleOptimizationNested.RevisionWireName)] decimal Revision)
{
    private const string RevisionWireName = "@revision";
}

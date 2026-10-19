using System.Text.Json;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jQueryResponse
{
    public static void ValidateErrors(JsonElement response) => Neo4jQueryProtocol.ValidateErrors(response);

    public static void ValidateResponse(int statusCode, JsonElement response) => Neo4jQueryProtocol.ValidateResponse(statusCode, response);

    public static string RuntimeReadErrorCode(JsonElement response) => Neo4jQueryProtocol.RuntimeReadErrorCode(response);

    public static void ValidateConstraintCreation(JsonElement response) => Neo4jQueryProtocol.ValidateConstraintCreation(response);
}

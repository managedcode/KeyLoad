using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class Neo4jMutationOperations
{
    private const string NativeConstraintConflict = "Neo4j:Neo.ClientError.Schema.ConstraintValidationFailed";
    private const string DataProperty = "data";
    private const string ValuesProperty = "values";

    internal static async Task<OperationResult> ExecuteAsync(
        Func<string, object?, CancellationToken, Task<JsonDocument>> query, string label,
        Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        var statement = scenario switch
        {
            Scenario.DocumentWrite => $"CREATE (n:{label} {{id:$id,json:$json}}) RETURN count(n)",
            Scenario.DocumentUpdate => $"MATCH (n:{label} {{id:$id}}) SET n.json=$json RETURN count(n)",
            Scenario.DocumentDelete => $"MATCH (n:{label} {{id:$id}}) WITH n,n.id AS deletedId DETACH DELETE n RETURN count(deletedId)",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        try
        {
            using var response = await query(statement, new { id = document.Id, json = document.Json }, token);
            RequireAffected(response.RootElement.GetProperty(DataProperty).GetProperty(ValuesProperty), scenario);
        }
        catch (ComparisonFailureException error) when (scenario == Scenario.DocumentWrite && error.Message == NativeConstraintConflict)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict);
        }
        return new();
    }

    internal static void RequireAffected(JsonElement rows, Scenario scenario)
    {
        if (rows.GetArrayLength() != 1 || rows[0].GetArrayLength() != 1)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
        var count = rows[0][0].GetInt64();
        if (count == 0 && scenario is Scenario.DocumentUpdate or Scenario.DocumentDelete)
        {
            throw new ComparisonFailureException(scenario == Scenario.DocumentUpdate
                ? ComparisonMutationFailures.UpdateMissing : ComparisonMutationFailures.DeleteMissing);
        }
        if (count != 1)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
    }
}

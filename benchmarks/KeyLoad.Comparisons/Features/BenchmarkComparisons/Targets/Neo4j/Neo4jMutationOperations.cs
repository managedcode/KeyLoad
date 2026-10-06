using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class Neo4jMutationOperations
{
    private const string ExecuteAsyncCREATENText = "CREATE (n:";
    private const string ExecuteAsyncIdIdJsonJsonRETURNCountNText = " {id:$id,json:$json}) RETURN count(n)";
    private const string ExecuteAsyncMATCHNText = "MATCH (n:";
    private const string ExecuteAsyncIdIdSETNJsonJsonRETURNCountNText = " {id:$id}) SET n.json=$json RETURN count(n)";
    private const string ExecuteAsyncIdIdWITHNNIdASDeletedIdDETACHDELETENRETURNCountDeletedIdText = " {id:$id}) WITH n,n.id AS deletedId DETACH DELETE n RETURN count(deletedId)";

    private const string NativeConstraintConflict = "Neo4j:Neo.ClientError.Schema.ConstraintValidationFailed";
    private const string DataProperty = "data";
    private const string ValuesProperty = "values";

    internal static async Task<OperationResult> ExecuteAsync(
        Func<string, object?, CancellationToken, Task<JsonDocument>> query, string label,
        Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        var statement = scenario switch
        {
            Scenario.DocumentWrite => $"{ExecuteAsyncCREATENText}{label}{ExecuteAsyncIdIdJsonJsonRETURNCountNText}",
            Scenario.DocumentUpdate => $"{ExecuteAsyncMATCHNText}{label}{ExecuteAsyncIdIdSETNJsonJsonRETURNCountNText}",
            Scenario.DocumentDelete => $"{ExecuteAsyncMATCHNText}{label}{ExecuteAsyncIdIdWITHNNIdASDeletedIdDETACHDELETENRETURNCountDeletedIdText}",
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
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;
        const int NoObservedItems = 0;

        if (rows.GetArrayLength() != SingleItemCount || rows[FirstElementIndex].GetArrayLength() != SingleItemCount)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
        var count = rows[FirstElementIndex][FirstElementIndex].GetInt64();
        if (count == NoObservedItems && scenario is Scenario.DocumentUpdate or Scenario.DocumentDelete)
        {
            throw new ComparisonFailureException(scenario == Scenario.DocumentUpdate
                ? ComparisonMutationFailures.UpdateMissing : ComparisonMutationFailures.DeleteMissing);
        }
        if (count != SingleItemCount)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
    }
}

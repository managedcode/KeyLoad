using System.Net.Http.Json;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class DocumentNeo4jProtocol
{
    private const string DataProperty = "data";
    private const string ValuesProperty = "values";
    internal static JsonElement Rows(JsonDocument response) => response.RootElement.GetProperty(DataProperty).GetProperty(ValuesProperty);
    internal static async Task<JsonDocument> QueryAsync(HttpClient operationClient, string statement, object? parameters, NativeComparisonExecutionOptions execution, CancellationToken cancellationToken)
    {
        const string DbNeo4jQueryV2Token = "db/neo4j/query/v2";

        using var response = await operationClient.PostAsJsonAsync(DbNeo4jQueryV2Token, new { statement, parameters = parameters ?? new { }, maxExecutionTime = execution.Neo4jMaximumExecutionTimeSeconds }, cancellationToken);
        Neo4jQueryProtocol.RequireQueryStatus((int)response.StatusCode);
        JsonDocument? json = null;
        try
        {
            json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            Neo4jQueryProtocol.ValidateResponse((int)response.StatusCode, json.RootElement);
            return json;
        }
        catch (JsonException)
        {
            json?.Dispose();
            throw Neo4jQueryProtocol.Invalid();
        }
        catch (Exception)
        {
            json?.Dispose();
            throw;
        }
    }
}

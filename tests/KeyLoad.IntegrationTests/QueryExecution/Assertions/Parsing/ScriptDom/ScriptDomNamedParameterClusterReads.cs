using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.QueryExecution.ScriptDomNamedParameterClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ScriptDomNamedParameterClusterReads
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SqlParserAdmissionClusterState state, CancellationToken token)
    {
        foreach (var name in new[] { P.Count, P.Collection, P.Stream, P.Generation })
        {
            var window = name == P.Count;
            var correct = Parameters(window);
            var mismatched = correct.ToDictionary(entry => entry.Key == name ? name.ToUpperInvariant() : entry.Key,
                entry => entry.Value, StringComparer.Ordinal);
            var sql = window ? P.CountSql : P.ModelSql;
            await SqlParserAdmissionClusterCurrent.ObserveAsync(root, state, async () =>
            {
                for (var route = 0; route < P.Routes; route++)
                {
                    await RefusedAsync(reader, state, sql, mismatched, route, token);
                    var page = await ReadAsync(reader, state, sql, correct, route, token);
                    await Assert.That(page.CutPosition).IsGreaterThan(0L);
                    await SqlClusterProtocol.EqualAsync(Expected(window, page.CutPosition), page);
                }
            }, token);
        }
    }
    private static async Task RefusedAsync(RequestCqrsClusterCallers reader, SqlParserAdmissionClusterState state,
        string sql, Dictionary<string, JsonElement> parameters, int route, CancellationToken token)
    {
        var query = new QueryRequest(state.Partition, sql, parameters, AllowFullScan: true);
        var envelope = new SqlOperationRequest(state.Partition, sql, parameters, AllowFullScan: true);
        switch (route)
        {
            case 0:
                await ScriptDomNamedParameterClusterRefusals.QueryAsync(await reader.Sdk.QueryAsync(query, token));
                break;
            case 1:
                await ScriptDomNamedParameterClusterRefusals.McpAsync(await reader.Mcp.CallAsync(
                    McpCallerTools.QueryExecute, query, token), state.Original.Identity.Secret, dispatched: true);
                break;
            case 2:
                await ScriptDomNamedParameterClusterRefusals.UnifiedAsync(await reader.Sdk.ExecuteSqlAsync(envelope, token), P.InvalidQuery);
                break;
            case 3:
                await ScriptDomNamedParameterClusterRefusals.McpAsync(await reader.Mcp.CallAsync(
                    SqlOperationProtocol.ToolName, envelope, token), state.Original.Identity.Secret, dispatched: true);
                break;
        }
    }
    private static async Task<QueryPage> ReadAsync(RequestCqrsClusterCallers reader, SqlParserAdmissionClusterState state,
        string sql, Dictionary<string, JsonElement> parameters, int route, CancellationToken token)
    {
        var query = new QueryRequest(state.Partition, sql, parameters, AllowFullScan: true);
        var envelope = new SqlOperationRequest(state.Partition, sql, parameters, AllowFullScan: true);
        return route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(query, token)),
            1 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, query, token))).Value,
            2 => await SqlClusterProtocol.SdkAsync<QueryPage>(reader.Sdk, envelope, token),
            3 => await SqlClusterProtocol.McpAsync<QueryPage>(reader.Mcp, envelope, token),
            _ => throw new InvalidOperationException()
        };
    }
    private static Dictionary<string, JsonElement> Parameters(bool window)
        => window ? new(StringComparer.Ordinal)
        {
            [P.Count] = JsonSerializer.SerializeToElement(P.Rows)
        } : new(StringComparer.Ordinal)
        {
            [P.Collection] = JsonSerializer.SerializeToElement(SqlParserAdmissionClusterState.EventResource),
            [P.Stream] = JsonSerializer.SerializeToElement(P.Source),
            [P.Generation] = JsonSerializer.SerializeToElement(P.FirstGeneration)
        };
    private static QueryPage Expected(bool window, long cut)
        => window ? new([new("a", 1L, """{"title":"first"}""", false, []),
            new("b", 1L, """{"title":"second"}""", false, []),
            new("c", 1L, """{"title":"third"}""", false, []),
            new("d", 1L, """{"title":"fourth"}""", false, [])], null, cut, "bounded-full-scan")
        : new([new("created", 1L, """{"value":1}""", false, []),
            new("updated", 2L, """{"value":2}""", false, [])], null, cut, "model-scan:events");
}

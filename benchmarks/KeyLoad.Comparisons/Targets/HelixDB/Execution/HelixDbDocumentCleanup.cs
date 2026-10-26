using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;

internal static class HelixDbDocumentCleanup
{
    private const int BeforeFirst = -1, Empty = 0, Single = 1;
    private const string Invalid = "HelixDbCleanupCardinalityMismatch";

    internal static async Task RunAsync(HttpClient http, string label, ComparisonExecutionOptions policy,
        TimeProvider clock, CancellationToken token)
    {
        var after = BeforeFirst;
        while (true)
        {
            using var page = await HelixDbProtocol.QueryAsync(http,
                HelixDbProtocol.Batch(HelixDbDocumentAst.Page(label, after, policy.ReadbackBatchCapacity), false),
                false, policy, clock, token).ConfigureAwait(false);
            var rows = HelixDbProtocol.Rows(page);
            if (rows.GetArrayLength() == Empty)
            {
                return;
            }
            if (rows.GetArrayLength() > policy.ReadbackBatchCapacity)
            {
                throw new ComparisonFailureException(Invalid);
            }
            foreach (var row in rows.EnumerateArray())
            {
                var number = row.GetProperty(HelixDbTokens.TokenNumber).GetInt32();
                if (number <= after)
                {
                    throw new ComparisonFailureException(Invalid);
                }
                after = number;
            }
            await DeletePageAsync(http, label, rows, policy, clock, token).ConfigureAwait(false);
        }
    }

    private static async Task DeletePageAsync(HttpClient http, string label, JsonElement rows,
        ComparisonExecutionOptions policy, TimeProvider clock, CancellationToken token)
    {
        var entries = new JsonArray();
        var names = new JsonArray();
        foreach (var row in rows.EnumerateArray())
        {
            var id = row.GetProperty(HelixDbTokens.TokenId).GetString()!;
            entries.Add(HelixDbProtocol.Entry(HelixDbProtocol.Node(HelixDbTokens.TokenCount,
                new() { [HelixDbTokens.TokenInput] = HelixDbDocumentAst.Lookup(label, id) }), id));
            entries.Add(HelixDbProtocol.Entry(HelixDbProtocol.Node(HelixDbTokens.TokenDrop,
                new() { [HelixDbTokens.TokenInput] = HelixDbDocumentAst.Lookup(label, id) }), HelixDbTokens.TokenRemoved + id));
            names.Add(id);
        }
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names),
            true, policy, clock, token).ConfigureAwait(false);
        foreach (var name in names)
        {
            var count = response.RootElement.GetProperty(name!.GetValue<string>());
            if ((count.ValueKind == JsonValueKind.Array ? count[Empty].GetInt64() : count.GetInt64()) != Single)
            {
                throw new ComparisonFailureException(Invalid);
            }
        }
    }
}

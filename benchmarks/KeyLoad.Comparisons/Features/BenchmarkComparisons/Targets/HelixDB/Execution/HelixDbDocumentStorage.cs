using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;

internal static class HelixDbDocumentStorage
{
    private const char IdentitySeparator = '-';
    private const char NativeIdentitySeparator = '_';
    internal static async Task SeedAsync(HttpClient http, string label, IComparisonCorpus dataset, NativeComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        foreach (var batch in dataset.Documents.Chunk(policy.WriteBatchCapacity))
        {
            var entries = new JsonArray();
            var names = new JsonArray();
            foreach (var document in batch)
            {
                var name = document.Id;
                entries.Add(HelixDbProtocol.Entry(HelixDbDocumentAst.Add(label, document), name));
                names.Add(name);
            }

            using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names), true, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        }

        foreach (var batch in dataset.Edges.Chunk(policy.WriteBatchCapacity))
        {
            await SeedEdgesAsync(http, label, batch, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        }
    }

    private static async Task SeedEdgesAsync(HttpClient http, string label, BenchmarkEdge[] edges, NativeComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        var entries = new JsonArray();
        var names = new JsonArray();
        foreach (var edge in edges)
        {
            var suffix = edge.Id.Replace(IdentitySeparator, NativeIdentitySeparator);
            var from = HelixDbNativeTokens.TokenFrom + suffix;
            var to = HelixDbNativeTokens.TokenToVariablePrefix + suffix;
            entries.Add(HelixDbProtocol.Entry(HelixDbDocumentAst.Lookup(label, edge.From), from));
            entries.Add(HelixDbProtocol.Entry(HelixDbDocumentAst.Lookup(label, edge.To), to));
            var relation = HelixDbProtocol.Node(HelixDbNativeTokens.TokenAddE, new() { [HelixDbNativeTokens.TokenInput] = HelixDbProtocol.Node(HelixDbNativeTokens.TokenNodes, new() { [HelixDbNativeTokens.TokenReference] = new JsonObject { [HelixDbNativeTokens.TokenVar] = from } }), [HelixDbNativeTokens.TokenLabel] = label + HelixDbNativeTokens.TokenLinks, [HelixDbNativeTokens.TokenTo] = new JsonObject { [HelixDbNativeTokens.TokenVar] = to }, [HelixDbNativeTokens.TokenProperties] = new JsonArray() });
            entries.Add(HelixDbProtocol.Entry(relation, suffix));
            names.Add(suffix);
        }

        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names), true, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
    }
}

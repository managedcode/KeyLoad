using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;

internal static class HelixDbDocumentStorage
{
    private const char IdentitySeparator = '-';
    private const char RuntimeIdentitySeparator = '_';
    internal static async Task SeedAsync(HttpClient http, string label, IComparisonCorpus dataset, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
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

    private static async Task SeedEdgesAsync(HttpClient http, string label, BenchmarkEdge[] edges, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        var entries = new JsonArray();
        var names = new JsonArray();
        foreach (var edge in edges)
        {
            var suffix = edge.Id.Replace(IdentitySeparator, RuntimeIdentitySeparator);
            var from = HelixDbTokens.TokenFrom + suffix;
            var to = HelixDbTokens.TokenToVariablePrefix + suffix;
            entries.Add(HelixDbProtocol.Entry(HelixDbDocumentAst.Lookup(label, edge.From), from));
            entries.Add(HelixDbProtocol.Entry(HelixDbDocumentAst.Lookup(label, edge.To), to));
            var relation = HelixDbProtocol.Node(HelixDbTokens.TokenAddE, new()
            {
                [HelixDbTokens.TokenInput] = HelixDbProtocol.Node(HelixDbTokens.TokenNodes, new()
                {
                    [HelixDbTokens.TokenReference] = new JsonObject { [HelixDbTokens.TokenVar] = from }
                }),
                [HelixDbTokens.TokenLabel] = label + HelixDbTokens.TokenLinks,
                [HelixDbTokens.TokenTo] = new JsonObject { [HelixDbTokens.TokenVar] = to },
                [HelixDbTokens.TokenProperties] = new JsonArray()
            });
            entries.Add(HelixDbProtocol.Entry(relation, suffix));
            names.Add(suffix);
        }

        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names), true, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
    }
}

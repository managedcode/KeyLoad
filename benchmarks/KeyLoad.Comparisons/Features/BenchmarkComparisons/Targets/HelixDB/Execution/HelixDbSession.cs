using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
internal sealed class HelixDbSession(HttpClient http, string label, int count, int depth, NativeComparisonExecutionOptions policy) : IComparisonSession
{
    private const int EmptyResultCount = 0;
    private const int SingleResultCardinality = 1;
    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        using var response = await QueryAsync(HelixDbDocumentAst.Read(label, document.Id), false, cancellationToken).ConfigureAwait(false);
        var rows = HelixDbProtocol.Rows(response);
        return rows.GetArrayLength() == EmptyResultCount ? null : Read(rows[EmptyResultCount]);
    }

    public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var after = -SingleResultCardinality;
        var seen = EmptyResultCount;
        while (true)
        {
            using var response = await QueryAsync(HelixDbDocumentAst.Page(label, after, policy.ReadbackBatchCapacity), false, cancellationToken).ConfigureAwait(false);
            var rows = HelixDbProtocol.Rows(response);
            if (rows.GetArrayLength() == EmptyResultCount)
            {
                break;
            }

            foreach (var row in rows.EnumerateArray())
            {
                var number = row.GetProperty(HelixDbNativeTokens.TokenNumber).GetInt32();
                if (number <= after)
                {
                    throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbCorpusOrderMismatch);
                }

                after = number;
                seen++;
                yield return Read(row);
            }
        }

        if (seen != count)
        {
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenScaledCorpusReadbackCountMismatch);
        }
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
    {
        if (scenario == Scenario.PointRead)
        {
            return new(Document: await ReadAsync(document, cancellationToken).ConfigureAwait(false));
        }

        if (scenario is Scenario.GraphNeighbors or Scenario.GraphTraverse)
        {
            using var response = await QueryAsync(HelixDbDocumentAst.Graph(label, document.Id, scenario == Scenario.GraphNeighbors ? SingleResultCardinality : depth), false, cancellationToken).ConfigureAwait(false);
            return new(Vertices: HelixDbProtocol.Rows(response).EnumerateArray().Select(row => row.GetProperty(HelixDbNativeTokens.TokenId).GetString()!).ToImmutableArray());
        }

        await MutateAsync(scenario, document, cancellationToken).ConfigureAwait(false);
        return new();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private async Task MutateAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        if (scenario == Scenario.DocumentDelete)
        {
            var entries = new JsonArray
            {
                HelixDbProtocol.Entry(HelixDbProtocol.Node(HelixDbNativeTokens.TokenCount, new() { [HelixDbNativeTokens.TokenInput] = HelixDbDocumentAst.Lookup(label, document.Id) }), HelixDbNativeTokens.TokenAffected),
                HelixDbProtocol.Entry(HelixDbProtocol.Node(HelixDbNativeTokens.TokenDrop, new() { [HelixDbNativeTokens.TokenInput] = HelixDbDocumentAst.Lookup(label, document.Id) }), HelixDbNativeTokens.TokenRemoved)
            };
            using var result = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, [HelixDbNativeTokens.TokenAffected]), true, policy, token).ConfigureAwait(false);
            RequireOne(result.RootElement.GetProperty(HelixDbNativeTokens.TokenAffected), scenario);
            return;
        }

        var mutation = scenario switch
        {
            Scenario.DocumentWrite => HelixDbDocumentAst.Add(label, document),
            Scenario.DocumentUpdate => HelixDbDocumentAst.Update(label, document),
            _ => throw new NotSupportedException()};
        using var response = await QueryAsync(HelixDbProtocol.Node(HelixDbNativeTokens.TokenCount, new() { [HelixDbNativeTokens.TokenInput] = mutation }), true, token).ConfigureAwait(false);
        RequireOne(response.RootElement.GetProperty(HelixDbNativeTokens.TokenRows), scenario);
    }

    private Task<JsonDocument> QueryAsync(JsonObject ast, bool write, CancellationToken token) => HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(ast, write), write, policy, token);
    private static FoundDocument Read(JsonElement row) => new(row.GetProperty(HelixDbNativeTokens.TokenId).GetString()!, row.GetProperty(HelixDbNativeTokens.TokenPayload).GetString()!);
    private static void RequireOne(JsonElement result, Scenario scenario)
    {
        var count = result.ValueKind == JsonValueKind.Array && result.GetArrayLength() == SingleResultCardinality ? result[EmptyResultCount].GetInt64() : result.GetInt64();
        if (count != SingleResultCardinality)
        {
            throw new ComparisonFailureException(scenario == Scenario.DocumentUpdate ? ComparisonMutationFailures.UpdateMissing : ComparisonMutationFailures.DeleteMissing);
        }
    }
}

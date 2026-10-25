using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class HelixDbSession(HttpClient http, string label, int count, int depth, IOptions<ComparisonExecutionOptions> executionOptions, TimeProvider? provider = null)
    : IComparisonSession, IDocumentBulkSession
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private ComparisonExecutionOptions Policy => executionOptions.Value;
    public async Task<(bool HasValue, FoundDocument? Value)> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        using var response = await QueryAsync(HelixDbDocumentAst.Read(label, document.Id), false, cancellationToken).ConfigureAwait(false);
        var rows = HelixDbProtocol.Rows(response);
        if (rows.GetArrayLength() > 1)
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbInvalidRows);
        }
        return rows.GetArrayLength() == 0 ? (false, default(FoundDocument?)) : (true, ((FoundDocument?)(Read(rows[0]))));
    }

    public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var after = -1;
        var seen = 0;
        while (true)
        {
            using var response = await QueryAsync(HelixDbDocumentAst.Page(label, after, Policy.ReadbackBatchCapacity), false, cancellationToken).ConfigureAwait(false);
            var rows = HelixDbProtocol.Rows(response);
            if (rows.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var row in rows.EnumerateArray())
            {
                var number = row.GetProperty(HelixDbTokens.TokenNumber).GetInt32();
                if (number <= after || (count >= 0 && seen >= count))
                {
                    throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbCorpusOrderMismatch);
                }

                after = number;
                seen++;
                yield return Read(row);
            }
        }

        if (count >= 0 && seen != count)
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenScaledCorpusReadbackCountMismatch);
        }
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
    {
        if (scenario == Scenario.PointRead)
        {
            return new(Document: (await ReadAsync(document, cancellationToken).ConfigureAwait(false)).Value);
        }

        if (scenario is Scenario.GraphNeighbors or Scenario.GraphTraverse)
        {
            using var response = await QueryAsync(HelixDbDocumentAst.Graph(label, document.Id, scenario == Scenario.GraphNeighbors ? 1 : depth), false, cancellationToken).ConfigureAwait(false);
            return new(Vertices: HelixDbProtocol.Rows(response).EnumerateArray().Select(row => row.GetProperty(HelixDbTokens.TokenId).GetString()!).ToImmutableArray());
        }

        await MutateAsync(scenario, document, cancellationToken).ConfigureAwait(false);
        return new();
    }

    public async Task CreateDocumentsAsync(IReadOnlyList<BenchmarkDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var entries = new JsonArray();
        var names = new JsonArray();
        foreach (var document in documents)
        {
            entries.Add(HelixDbProtocol.Entry(HelixDbProtocol.Node(HelixDbTokens.TokenCount, new()
            {
                [HelixDbTokens.TokenInput] = HelixDbDocumentAst.Add(label, document)
            }), document.Id));
            names.Add(document.Id);
        }

        // The write batch commits or rolls back together; the unique id index rejects an existing document as CreateConflict.
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names), true, Policy,
            token: cancellationToken, create: true, timeProvider: timeProvider).ConfigureAwait(false);
        foreach (var document in documents)
        {
            if (!response.RootElement.TryGetProperty(document.Id, out var created) || Count(created) != 1)
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private async Task MutateAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        if (scenario == Scenario.DocumentDelete)
        {
            var entries = new JsonArray
            {
                HelixDbProtocol.Entry(HelixDbProtocol.Node(HelixDbTokens.TokenCount, new() { [HelixDbTokens.TokenInput] = HelixDbDocumentAst.Lookup(label, document.Id) }), HelixDbTokens.TokenAffected),
                HelixDbProtocol.Entry(HelixDbProtocol.Node(HelixDbTokens.TokenDrop, new() { [HelixDbTokens.TokenInput] = HelixDbDocumentAst.Lookup(label, document.Id) }), HelixDbTokens.TokenRemoved)
            };
            using var result = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, [HelixDbTokens.TokenAffected]), true, Policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
            RequireOne(result.RootElement.GetProperty(HelixDbTokens.TokenAffected), scenario);
            return;
        }

        var mutation = scenario switch
        {
            Scenario.DocumentWrite => HelixDbDocumentAst.Add(label, document),
            Scenario.DocumentUpdate => HelixDbDocumentAst.Update(label, document),
            _ => throw new NotSupportedException()
        };
        using var response = await HelixDbProtocol.QueryAsync(http,
            HelixDbProtocol.Batch(HelixDbProtocol.Node(HelixDbTokens.TokenCount, new()
            {
                [HelixDbTokens.TokenInput] = mutation
            }), true),
            true, Policy, token: token, create: scenario == Scenario.DocumentWrite, timeProvider: timeProvider).ConfigureAwait(false);
        RequireOne(response.RootElement.GetProperty(HelixDbTokens.TokenRows), scenario);
    }

    private Task<JsonDocument> QueryAsync(JsonObject ast, bool write, CancellationToken token) => HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(ast, write), write, Policy, token: token, timeProvider: timeProvider);
    private static FoundDocument Read(JsonElement row) => new(row.GetProperty(HelixDbTokens.TokenId).GetString()!, row.GetProperty(HelixDbTokens.TokenPayload).GetString()!);
    private static void RequireOne(JsonElement result, Scenario scenario)
    {
        if (Count(result) != 1)
        {
            throw new ComparisonFailureException(scenario == Scenario.DocumentUpdate ? ComparisonMutationFailures.UpdateMissing : ComparisonMutationFailures.DeleteMissing);
        }
    }

    private static long Count(JsonElement result) => result.ValueKind == JsonValueKind.Array && result.GetArrayLength() == 1 ? result[0].GetInt64() : result.GetInt64();
}

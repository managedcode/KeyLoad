using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class SurrealDbSession(HttpClient http, string table, string edge, int count, int depth, int topK, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider? provider = null) : IComparisonSession
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private NativeComparisonExecutionOptions Policy => executionOptions.Value;
    private const string NativeSELECTKeyPayloadFROMFormatTemplate = "SELECT key, payload FROM {0}:{1};";
    private static readonly System.Text.CompositeFormat NativeSELECTKeyPayloadFROMFormat = System.Text.CompositeFormat.Parse(NativeSELECTKeyPayloadFROMFormatTemplate);
    private const int EmptyResultCount = 0;
    private const int SingleResultCardinality = 1;
    private const string InvalidPointReadCardinality = "SurrealDbPointReadCardinalityMismatch";
    private const string NativeSELECTKeyPayloadNumberFROMWHERENumberORDERFormatTemplate = "SELECT key, payload, number FROM {0} WITH INDEX {3} WHERE number > {1} ORDER BY number LIMIT {2};";
    private static readonly System.Text.CompositeFormat NativeSELECTKeyPayloadNumberFROMWHERENumberORDERFormat = System.Text.CompositeFormat.Parse(NativeSELECTKeyPayloadNumberFROMWHERENumberORDERFormatTemplate);
    private const string NativeSELECTKeyPayloadFROMWHEREEmbeddingCOSINEORDERFormatTemplate = "SELECT key, payload FROM {0} WHERE embedding <|{1},COSINE|> {2} ORDER BY vector::distance::knn(), key LIMIT {3};";
    private static readonly System.Text.CompositeFormat NativeSELECTKeyPayloadFROMWHEREEmbeddingCOSINEORDERFormat = System.Text.CompositeFormat.Parse(NativeSELECTKeyPayloadFROMWHEREEmbeddingCOSINEORDERFormatTemplate);
    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        using var response = await SurrealDbSqlTransport.QueryAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeSELECTKeyPayloadFROMFormat, table, SurrealDbDocumentSql.Key(document.Id)), Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var rows = SurrealDbVectorProtocol.SingleResult(response.RootElement);
        if (rows.GetArrayLength() > SingleResultCardinality)
        {
            throw new InvalidDataException(InvalidPointReadCardinality);
        }
        return rows.GetArrayLength() == EmptyResultCount ? null : Read(rows[EmptyResultCount]);
    }

    public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var after = -SingleResultCardinality;
        var seen = EmptyResultCount;
        while (true)
        {
            using var response = await SurrealDbSqlTransport.QueryAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeSELECTKeyPayloadNumberFROMWHERENumberORDERFormat, table, after, Policy.ReadbackBatchCapacity, SurrealDbReadbackIndex.Name(table)), Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            var rows = SurrealDbVectorProtocol.SingleResult(response.RootElement);
            if (rows.GetArrayLength() == EmptyResultCount)
            {
                break;
            }

            foreach (var row in rows.EnumerateArray())
            {
                var number = row.GetProperty(SurrealDbNativeTokens.TokenNumber).GetInt32();
                if (number <= after || (count >= EmptyResultCount && seen >= count))
                {
                    throw new ComparisonFailureException(SurrealDbNativeTokens.TokenSurrealDbCorpusOrderMismatch);
                }

                after = number;
                seen++;
                yield return Read(row);
            }
        }

        if (count >= EmptyResultCount && seen != count)
        {
            throw new ComparisonFailureException(SurrealDbNativeTokens.TokenScaledCorpusReadbackCountMismatch);
        }
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
    {
        if (scenario == Scenario.PointRead)
        {
            return new(Document: await ReadAsync(document, cancellationToken).ConfigureAwait(false));
        }

        if (scenario is Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete)
        {
            await MutateAsync(scenario, document, cancellationToken).ConfigureAwait(false);
            return new();
        }

        if (scenario is Scenario.GraphNeighbors or Scenario.GraphTraverse)
        {
            using var response = await SurrealDbSqlTransport.QueryAsync(http, SurrealDbDocumentSql.Graph(table, edge, document.Id, scenario == Scenario.GraphNeighbors ? SingleResultCardinality : depth), Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            return new(Vertices: SurrealDbVectorProtocol.SingleResult(response.RootElement).EnumerateArray().Select(id => id.GetString()!).ToImmutableArray());
        }

        if (scenario == Scenario.VectorExact)
        {
            using var response = await SurrealDbSqlTransport.QueryAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeSELECTKeyPayloadFROMWHEREEmbeddingCOSINEORDERFormat, table, topK, JsonSerializer.Serialize(document.Vector), topK), Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            return new(Neighbors: SurrealDbVectorProtocol.SingleResult(response.RootElement).EnumerateArray().Select(Read).ToImmutableArray());
        }

        throw new NotSupportedException();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private async Task MutateAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        using var response = await SurrealDbSqlTransport.QueryAsync(http, SurrealDbDocumentSql.Mutation(table, scenario, document), Policy, cancellationToken: token, create: scenario == Scenario.DocumentWrite, timeProvider: timeProvider).ConfigureAwait(false);
        var result = SurrealDbVectorProtocol.SingleResult(response.RootElement);
        var affected = result.ValueKind == JsonValueKind.Object ? SingleResultCardinality : result.GetArrayLength();
        if (affected != SingleResultCardinality)
        {
            throw new ComparisonFailureException(scenario == Scenario.DocumentUpdate ? ComparisonMutationFailures.UpdateMissing : ComparisonMutationFailures.DeleteMissing);
        }
    }

    private FoundDocument Read(JsonElement row) => new(row.GetProperty(SurrealDbNativeTokens.TokenKey).GetString()!, row.GetProperty(SurrealDbNativeTokens.TokenPayload).GetString()!);
}

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;
internal static class SurrealDbVectorStorage
{
    private const int SingleResultCardinality = 1;
    private const int EmptyResultCount = 0;
    private const string InvalidResponse = "SurrealDbInvalidResponse";
    private const string NumberKey = "number";
    private const string IdKey = "id";
    private const string EmbeddingKey = "embedding";
    private const string PayloadKey = "payload";
    internal static async IAsyncEnumerable<VectorReadback> ReadbackAsync(HttpClient http, string table, NativeComparisonExecutionOptions policy, int corpusCount, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = EmptyResultCount;
        var after = -SingleResultCardinality;
        while (true)
        {
            using var response = await SurrealDbSqlTransport.QueryAsync(http, SurrealDbVectorProtocol.ReadbackSql(table, after, policy.ReadbackBatchCapacity), policy, cancellationToken).ConfigureAwait(false);
            var rows = SurrealDbVectorProtocol.SingleResult(response.RootElement);
            if (rows.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(InvalidResponse);
            }

            if (rows.GetArrayLength() == EmptyResultCount)
            {
                break;
            }

            foreach (var row in rows.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var number = row.GetProperty(NumberKey).GetInt32();
                var id = SurrealDbVectorProtocol.ReadRecordId(table, row.GetProperty(IdKey));
                var embedding = SurrealDbVectorProtocol.ReadVector(row.GetProperty(EmbeddingKey));
                var payload = SurrealDbVectorProtocol.ReadRequiredString(row, PayloadKey);
                after = number;
                seen++;
                yield return new(number, id, embedding.Length, VectorComparisonCorpus.HashVector(embedding), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
            }
        }

        if (seen != corpusCount)
        {
            throw new InvalidDataException(SurrealDbNativeTokens.TokenSurrealDbReadbackCountMismatch);
        }
    }

    internal static async Task<VectorReadback?> ReadAsync(HttpClient http, string table, NativeComparisonExecutionOptions policy, string id, CancellationToken cancellationToken)
    {
        using var response = await SurrealDbSqlTransport.QueryAsync(http, SurrealDbVectorProtocol.ReadOneSql(table, id), policy, cancellationToken).ConfigureAwait(false);
        var result = SurrealDbVectorProtocol.SingleResult(response.RootElement);
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == EmptyResultCount)
        {
            return null;
        }

        var row = result[EmptyResultCount];
        var vector = SurrealDbVectorProtocol.ReadVector(row.GetProperty(EmbeddingKey));
        return new(row.GetProperty(NumberKey).GetInt32(), SurrealDbVectorProtocol.ReadRecordId(table, row.GetProperty(IdKey)), vector.Length, VectorComparisonCorpus.HashVector(vector), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SurrealDbVectorProtocol.ReadRequiredString(row, PayloadKey)))));
    }
}

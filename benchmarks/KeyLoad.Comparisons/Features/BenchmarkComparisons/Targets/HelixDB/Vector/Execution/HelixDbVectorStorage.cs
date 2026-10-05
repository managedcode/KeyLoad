using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Owns bounded ingestion and native ordered vector/value readback.</summary>
internal static class HelixDbVectorStorage
{
    private const int EmptyResultCount = 0;
    private const int SingleResultCardinality = 1;
    internal static async Task<int> IngestAsync(HttpClient http, string label, IAsyncEnumerable<VectorDocument> documents, NativeComparisonExecutionOptions policy, CancellationToken token)
    {
        var entries = new JsonArray();
        var names = new JsonArray();
        var count = EmptyResultCount;
        await foreach (var document in documents.WithCancellation(token).ConfigureAwait(false))
        {
            var name = HelixDbNativeTokens.TokenD + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            entries.Add(HelixDbProtocol.Entry(HelixDbVectorAst.Add(label, document), name));
            names.Add(name);
            count++;
            if (entries.Count == policy.WriteBatchCapacity)
            {
                using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names), true, policy, token).ConfigureAwait(false);
                entries = [];
                names = [];
            }
        }

        if (entries.Count != EmptyResultCount)
        {
            using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names), true, policy, token).ConfigureAwait(false);
        }

        return count;
    }

    internal static async IAsyncEnumerable<VectorReadback> ReadbackAsync(HttpClient http, string label, int count, NativeComparisonExecutionOptions policy, [EnumeratorCancellation] CancellationToken token)
    {
        var after = -SingleResultCardinality;
        var seen = EmptyResultCount;
        while (true)
        {
            using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbVectorAst.Page(label, after, policy.ReadbackBatchCapacity), false), false, policy, token).ConfigureAwait(false);
            var rows = HelixDbProtocol.Rows(response);
            if (rows.GetArrayLength() == EmptyResultCount)
            {
                break;
            }

            foreach (var row in rows.EnumerateArray())
            {
                var result = Read(row);
                if (result.Number <= after || seen >= count)
                {
                    throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbReadbackOrderingInvalid);
                }

                after = result.Number;
                seen++;
                yield return result;
            }
        }

        if (seen != count)
        {
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbReadbackCountMismatch);
        }
    }

    internal static VectorReadback Read(JsonElement row)
    {
        var vector = row.GetProperty(HelixDbNativeTokens.TokenEmbedding).EnumerateArray().Select(component => component.GetSingle()).ToArray();
        if (vector.Length == EmptyResultCount || vector.Any(value => !float.IsFinite(value)))
        {
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbInvalidStoredVector);
        }

        return new(row.GetProperty(HelixDbNativeTokens.TokenNumber).GetInt32(), row.GetProperty(HelixDbNativeTokens.TokenId).GetString()!, vector.Length, VectorComparisonCorpus.HashVector(vector), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row.GetProperty(HelixDbNativeTokens.TokenPayload).GetString()!))));
    }
}

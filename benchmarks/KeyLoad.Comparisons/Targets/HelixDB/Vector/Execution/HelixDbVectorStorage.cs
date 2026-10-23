using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Owns bounded ingestion and execution ordered vector/value readback.</summary>
internal static class HelixDbVectorStorage
{
    internal static async Task<int> IngestAsync(HttpClient http, string label, IAsyncEnumerable<VectorDocument> documents, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        var entries = new JsonArray();
        var names = new JsonArray();
        var count = 0;
        await foreach (var document in documents.WithCancellation(token).ConfigureAwait(false))
        {
            var name = HelixDbTokens.TokenD + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            entries.Add(HelixDbProtocol.Entry(HelixDbVectorAst.Add(label, document), name));
            names.Add(name);
            count++;
            if (entries.Count == policy.WriteBatchCapacity)
            {
                using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names), true, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
                entries = [];
                names = [];
            }
        }

        if (entries.Count != 0)
        {
            using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(entries, true, names), true, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        }

        return count;
    }

    internal static async IAsyncEnumerable<VectorReadback> ReadbackAsync(HttpClient http, string label, int count, ComparisonExecutionOptions policy, TimeProvider timeProvider, [EnumeratorCancellation] CancellationToken token)
    {
        var after = -1;
        var seen = 0;
        while (true)
        {
            using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbVectorAst.Page(label, after, policy.ReadbackBatchCapacity), false), false, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
            var rows = HelixDbProtocol.Rows(response);
            if (rows.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var row in rows.EnumerateArray())
            {
                var result = Read(row);
                if (result.Number <= after || seen >= count)
                {
                    throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbReadbackOrderingInvalid);
                }

                after = result.Number;
                seen++;
                yield return result;
            }
        }

        if (seen != count)
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbReadbackCountMismatch);
        }
    }

    internal static VectorReadback Read(JsonElement row)
    {
        var vector = row.GetProperty(HelixDbTokens.TokenEmbedding).EnumerateArray().Select(component => component.GetSingle()).ToArray();
        if (vector.Length == 0 || vector.Any(value => !float.IsFinite(value)))
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbInvalidStoredVector);
        }

        return new(row.GetProperty(HelixDbTokens.TokenNumber).GetInt32(), row.GetProperty(HelixDbTokens.TokenId).GetString()!, vector.Length, VectorComparisonCorpus.HashVector(vector), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row.GetProperty(HelixDbTokens.TokenPayload).GetString()!))));
    }
}

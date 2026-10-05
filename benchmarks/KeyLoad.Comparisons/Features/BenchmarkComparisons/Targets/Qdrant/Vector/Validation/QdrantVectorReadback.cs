using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorReadback
{
    internal static VectorReadback Parse(JsonElement point)
    {
        var payload = point.GetProperty("payload");
        var number = payload.GetProperty("number").GetInt32();
        var id = payload.GetProperty("id").GetString()
            ?? throw new ComparisonFailureException("QdrantVectorReadbackIdentity");
        if (point.GetProperty("id").GetInt64() != (long)number + 1
            || id != "v" + number.ToString("D9", System.Globalization.CultureInfo.InvariantCulture)
            || payload.GetProperty("filtered").GetBoolean() != (number % 100 == 0)
            || payload.GetProperty("mixed").GetBoolean() != (number % 10 != 9))
            throw new ComparisonFailureException("QdrantVectorReadbackIdentity");
        var embedding = point.GetProperty("vector").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (embedding.Length != 128 || embedding.Any(value => !float.IsFinite(value)))
            throw new ComparisonFailureException("QdrantVectorReadbackDimensions");
        var document = payload.GetProperty("document").GetString()
            ?? throw new ComparisonFailureException("QdrantVectorReadbackPayload");
        return new(number, id, embedding.Length, VectorComparisonCorpus.HashVector(embedding),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document))));
    }

    internal static int Number(string id)
    {
        if (id.Length != 10 || id[0] != 'v'
            || !int.TryParse(id.AsSpan(1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number < 0)
            throw new ArgumentException("Invalid vector document identity.", nameof(id));
        return number;
    }
}

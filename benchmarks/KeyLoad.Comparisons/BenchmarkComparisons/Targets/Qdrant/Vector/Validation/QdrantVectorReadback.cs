using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorReadback
{
    internal static VectorReadback Parse(JsonElement point)
    {
        var payload = point.GetProperty(QdrantVectorProtocol.Payload);
        var number = payload.GetProperty(QdrantVectorProtocol.Number).GetInt32();
        var id = payload.GetProperty(QdrantVectorProtocol.Identifier).GetString()
            ?? throw new ComparisonFailureException(QdrantVectorProtocol.ReadbackIdentity);
        if (point.GetProperty(QdrantVectorProtocol.Identifier).GetInt64() != (long)number + QdrantVectorProtocol.PointOrdinalOffset
            || id != QdrantVectorProtocol.IdentityPrefix + number.ToString(VectorProfileTokens.NumberFormat, System.Globalization.CultureInfo.InvariantCulture)
            || payload.GetProperty(QdrantVectorProtocol.Filtered).GetBoolean() != (number % QdrantVectorProtocol.FilterDivisor == QdrantVectorProtocol.EmptyCount)
            || payload.GetProperty(QdrantVectorProtocol.Mixed).GetBoolean() != (number % QdrantVectorProtocol.MutableDivisor != QdrantVectorProtocol.MutableRemainder))
        {
            throw new ComparisonFailureException(QdrantVectorProtocol.ReadbackIdentity);
        }
        var embedding = point.GetProperty(QdrantVectorProtocol.Vector).EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (embedding.Length != QdrantVectorProtocol.VectorDimensions || embedding.Any(value => !float.IsFinite(value)))
        {
            throw new ComparisonFailureException(QdrantVectorProtocol.ReadbackDimensions);
        }
        var document = payload.GetProperty(QdrantVectorProtocol.Document).GetString()
            ?? throw new ComparisonFailureException(QdrantVectorProtocol.ReadbackPayload);
        return new(number, id, embedding.Length, VectorComparisonCorpus.HashVector(embedding),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document))));
    }

    internal static int Number(string id)
    {
        if (id.Length != QdrantVectorProtocol.ResultCount || id[QdrantVectorProtocol.FirstElementIndex] != QdrantVectorProtocol.IdentityFirstCharacter
            || !int.TryParse(id.AsSpan(QdrantVectorProtocol.IdentityDigitsOffset), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number < QdrantVectorProtocol.EmptyCount)
        {
            throw new ArgumentException(QdrantVectorProtocol.InvalidIdentity, nameof(id));
        }
        return number;
    }
}

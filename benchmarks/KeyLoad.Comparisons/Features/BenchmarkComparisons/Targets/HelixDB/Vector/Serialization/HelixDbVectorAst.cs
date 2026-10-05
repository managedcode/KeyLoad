using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Builds actual HelixDB vector, candidate and lifecycle operation trees.</summary>
internal static class HelixDbVectorAst
{
    private const int FilteredCorpusModulo = 100;
    private const int EmptyResultCount = 0;
    private const int CanonicalRecordIdWidth = 10;
    private const int ExcludedStableRemainder = 9;
    internal static JsonObject Add(string label, VectorDocument document)
    {
        var properties = new JsonArray
        {
            new JsonArray(HelixDbNativeTokens.TokenNumber, HelixDbProtocol.Value(HelixDbNativeTokens.TokenI64, document.Number)),
            new JsonArray(HelixDbNativeTokens.TokenId, HelixDbProtocol.Value(HelixDbNativeTokens.TokenString, document.Id)),
            new JsonArray(HelixDbNativeTokens.TokenPayload, HelixDbProtocol.Value(HelixDbNativeTokens.TokenString, document.Payload)),
            new JsonArray(HelixDbNativeTokens.TokenEmbedding, Vector(document.Embedding.Span)),
            new JsonArray(HelixDbNativeTokens.TokenFiltered, HelixDbProtocol.Value(HelixDbNativeTokens.TokenBool, document.Number % FilteredCorpusModulo == EmptyResultCount)),
            new JsonArray(HelixDbNativeTokens.TokenStable, HelixDbProtocol.Value(HelixDbNativeTokens.TokenBool, document.Number % CanonicalRecordIdWidth != ExcludedStableRemainder))
        };
        return HelixDbProtocol.Node(HelixDbNativeTokens.TokenAddN, new() { [HelixDbNativeTokens.TokenLabel] = label, [HelixDbNativeTokens.TokenProperties] = properties });
    }

    internal static JsonObject Search(string label, ReadOnlySpan<float> vector, int count, VectorQueryMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        var fields = new JsonObject
        {
            [HelixDbNativeTokens.TokenLabel] = label,
            [HelixDbNativeTokens.TokenProperty] = HelixDbNativeTokens.TokenEmbedding,
            [HelixDbNativeTokens.TokenQueryVector] = Vector(vector),
            [HelixDbNativeTokens.TokenK] = new JsonObject
            {
                [HelixDbNativeTokens.TokenLiteral] = count
            }
        };
        var operation = HelixDbNativeTokens.TokenVectorSearchNodes;
        if (mode != VectorQueryMode.Plain)
        {
            fields[HelixDbNativeTokens.TokenInput] = HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbNativeTokens.TokenEq, mode == VectorQueryMode.Filtered ? HelixDbNativeTokens.TokenFiltered : HelixDbNativeTokens.TokenStable, HelixDbNativeTokens.TokenBool, true));
            operation = HelixDbNativeTokens.TokenVectorSearchNodesWithin;
        }

        return HelixDbProtocol.Project(HelixDbProtocol.Node(operation, fields), HelixDbNativeTokens.TokenId, HelixDbNativeTokens.MetaTokenDistance);
    }

    internal static JsonObject Read(string label, string id) => HelixDbProtocol.Project(HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbNativeTokens.TokenEq, HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenString, id)), HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenNumber, HelixDbNativeTokens.TokenEmbedding, HelixDbNativeTokens.TokenPayload);
    internal static JsonObject Page(string label, int after, int count) => HelixDbProtocol.Project(HelixDbProtocol.Limit(HelixDbProtocol.Ordered(HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbNativeTokens.TokenGt, HelixDbNativeTokens.TokenNumber, HelixDbNativeTokens.TokenI64, after)), HelixDbNativeTokens.TokenNumber), count), HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenNumber, HelixDbNativeTokens.TokenEmbedding, HelixDbNativeTokens.TokenPayload);
    internal static JsonObject Update(string label, VectorUpdate update) => HelixDbProtocol.Node(HelixDbNativeTokens.TokenSetProperty, new() { [HelixDbNativeTokens.TokenInput] = HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbNativeTokens.TokenEq, HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenString, update.Id)), [HelixDbNativeTokens.TokenName] = HelixDbNativeTokens.TokenEmbedding, [HelixDbNativeTokens.TokenValue] = Vector(update.Embedding.Span) });
    internal static JsonObject Index(string label, int dimensions, bool drop = false)
    {
        var fields = new JsonObject
        {
            [HelixDbNativeTokens.TokenSpec] = HelixDbProtocol.Node(HelixDbNativeTokens.TokenNodeVector, new() { [HelixDbNativeTokens.TokenLabel] = label, [HelixDbNativeTokens.TokenProperty] = HelixDbNativeTokens.TokenEmbedding, [HelixDbNativeTokens.TokenDimension] = dimensions, [HelixDbNativeTokens.TokenMetric] = HelixDbNativeTokens.TokenCosine })
        };
        if (!drop)
        {
            fields[HelixDbNativeTokens.TokenIfNotExists] = false;
        }

        return HelixDbProtocol.Node(drop ? HelixDbNativeTokens.TokenDropIndex : HelixDbNativeTokens.TokenCreateIndex, fields);
    }

    private static JsonObject Vector(ReadOnlySpan<float> vector)
    {
        var values = new JsonArray();
        foreach (var value in vector)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(vector));
            }

            values.Add(value);
        }

        return HelixDbProtocol.Value(HelixDbNativeTokens.TokenF32Array, values);
    }
}

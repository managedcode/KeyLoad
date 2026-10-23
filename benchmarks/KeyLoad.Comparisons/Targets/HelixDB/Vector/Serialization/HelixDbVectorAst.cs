using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Builds actual HelixDB vector, candidate and lifecycle operation trees.</summary>
internal static class HelixDbVectorAst
{
    private const int FilteredCorpusModulo = 100;
    private const int CanonicalRecordIdWidth = 10;
    private const int ExcludedStableRemainder = 9;
    internal static JsonObject Add(string label, VectorDocument document)
    {
        var properties = new JsonArray
        {
            new JsonArray(HelixDbTokens.TokenNumber, HelixDbProtocol.Value(HelixDbTokens.TokenI64, document.Number)),
            new JsonArray(HelixDbTokens.TokenId, HelixDbProtocol.Value(HelixDbTokens.TokenString, document.Id)),
            new JsonArray(HelixDbTokens.TokenPayload, HelixDbProtocol.Value(HelixDbTokens.TokenString, document.Payload)),
            new JsonArray(HelixDbTokens.TokenEmbedding, Vector(document.Embedding.Span)),
            new JsonArray(HelixDbTokens.TokenFiltered, HelixDbProtocol.Value(HelixDbTokens.TokenBool, document.Number % FilteredCorpusModulo == 0)),
            new JsonArray(HelixDbTokens.TokenStable, HelixDbProtocol.Value(HelixDbTokens.TokenBool, document.Number % CanonicalRecordIdWidth != ExcludedStableRemainder))
        };
        return HelixDbProtocol.Node(HelixDbTokens.TokenAddN, new()
        {
            [HelixDbTokens.TokenLabel] = label,
            [HelixDbTokens.TokenProperties] = properties
        });
    }

    internal static JsonObject Search(string label, ReadOnlySpan<float> vector, int count, VectorQueryMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        var fields = new JsonObject
        {
            [HelixDbTokens.TokenLabel] = label,
            [HelixDbTokens.TokenProperty] = HelixDbTokens.TokenEmbedding,
            [HelixDbTokens.TokenQueryVector] = Vector(vector),
            [HelixDbTokens.TokenK] = new JsonObject
            {
                [HelixDbTokens.TokenLiteral] = count
            }
        };
        var operation = HelixDbTokens.TokenVectorSearchNodes;
        if (mode != VectorQueryMode.Plain)
        {
            fields[HelixDbTokens.TokenInput] = HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbTokens.TokenEq, mode == VectorQueryMode.Filtered ? HelixDbTokens.TokenFiltered : HelixDbTokens.TokenStable, HelixDbTokens.TokenBool, true));
            operation = HelixDbTokens.TokenVectorSearchNodesWithin;
        }

        return HelixDbProtocol.Project(HelixDbProtocol.Node(operation, fields), HelixDbTokens.TokenId, HelixDbTokens.MetaTokenDistance);
    }

    internal static JsonObject Read(string label, string id) => HelixDbProtocol.Project(HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbTokens.TokenEq, HelixDbTokens.TokenId, HelixDbTokens.TokenString, id)), HelixDbTokens.TokenId, HelixDbTokens.TokenNumber, HelixDbTokens.TokenEmbedding, HelixDbTokens.TokenPayload);
    internal static JsonObject Page(string label, int after, int count) => HelixDbProtocol.Project(HelixDbProtocol.Limit(HelixDbProtocol.Ordered(HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbTokens.TokenGt, HelixDbTokens.TokenNumber, HelixDbTokens.TokenI64, after)), HelixDbTokens.TokenNumber), count), HelixDbTokens.TokenId, HelixDbTokens.TokenNumber, HelixDbTokens.TokenEmbedding, HelixDbTokens.TokenPayload);
    internal static JsonObject Update(string label, VectorUpdate update) => HelixDbProtocol.Node(HelixDbTokens.TokenSetProperty, new() { [HelixDbTokens.TokenInput] = HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbTokens.TokenEq, HelixDbTokens.TokenId, HelixDbTokens.TokenString, update.Id)), [HelixDbTokens.TokenName] = HelixDbTokens.TokenEmbedding, [HelixDbTokens.TokenValue] = Vector(update.Embedding.Span) });
    internal static JsonObject Index(string label, int dimensions, bool drop = false)
    {
        var fields = new JsonObject
        {
            [HelixDbTokens.TokenSpec] = HelixDbProtocol.Node(HelixDbTokens.TokenNodeVector, new() { [HelixDbTokens.TokenLabel] = label, [HelixDbTokens.TokenProperty] = HelixDbTokens.TokenEmbedding, [HelixDbTokens.TokenDimension] = dimensions, [HelixDbTokens.TokenMetric] = HelixDbTokens.TokenCosine })
        };
        if (!drop)
        {
            fields[HelixDbTokens.TokenIfNotExists] = false;
        }

        return HelixDbProtocol.Node(drop ? HelixDbTokens.TokenDropIndex : HelixDbTokens.TokenCreateIndex, fields);
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

        return HelixDbProtocol.Value(HelixDbTokens.TokenF32Array, values);
    }
}

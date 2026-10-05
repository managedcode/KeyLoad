using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
internal static class HelixDbDocumentAst
{
    private const int SingleResultCardinality = 1;
    private const int EmptyResultCount = 0;
    internal static JsonObject Lookup(string label, string id) => HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbNativeTokens.TokenEq, HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenString, id));
    internal static JsonObject Read(string label, string id) => HelixDbProtocol.Project(Lookup(label, id), HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenPayload);
    internal static JsonObject Add(string label, BenchmarkDocument document) => HelixDbProtocol.Node(HelixDbNativeTokens.TokenAddN, new() { [HelixDbNativeTokens.TokenLabel] = label, [HelixDbNativeTokens.TokenProperties] = new JsonArray { new JsonArray(HelixDbNativeTokens.TokenId, HelixDbProtocol.Value(HelixDbNativeTokens.TokenString, document.Id)), new JsonArray(HelixDbNativeTokens.TokenNumber, HelixDbProtocol.Value(HelixDbNativeTokens.TokenI64, document.Number)), new JsonArray(HelixDbNativeTokens.TokenPayload, HelixDbProtocol.Value(HelixDbNativeTokens.TokenString, document.Json)) } });
    internal static JsonObject Update(string label, BenchmarkDocument document) => HelixDbProtocol.Node(HelixDbNativeTokens.TokenSetProperty, new() { [HelixDbNativeTokens.TokenInput] = Lookup(label, document.Id), [HelixDbNativeTokens.TokenName] = HelixDbNativeTokens.TokenPayload, [HelixDbNativeTokens.TokenValue] = HelixDbProtocol.Value(HelixDbNativeTokens.TokenString, document.Json) });
    internal static JsonObject Page(string label, int after, int capacity) => HelixDbProtocol.Project(HelixDbProtocol.Limit(HelixDbProtocol.Ordered(HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbNativeTokens.TokenGt, HelixDbNativeTokens.TokenNumber, HelixDbNativeTokens.TokenI64, after)), HelixDbNativeTokens.TokenNumber), capacity), HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenNumber, HelixDbNativeTokens.TokenPayload);
    internal static JsonObject Index(string label, string property, bool range) => HelixDbProtocol.Node(HelixDbNativeTokens.TokenCreateIndex, new() { [HelixDbNativeTokens.TokenIfNotExists] = false, [HelixDbNativeTokens.TokenSpec] = HelixDbProtocol.Node(range ? HelixDbNativeTokens.TokenNodeRange : HelixDbNativeTokens.TokenNodeEquality, range ? new() { [HelixDbNativeTokens.TokenLabel] = label, [HelixDbNativeTokens.TokenProperty] = property, [HelixDbNativeTokens.TokenDirection] = HelixDbNativeTokens.TokenAsc } : new() { [HelixDbNativeTokens.TokenLabel] = label, [HelixDbNativeTokens.TokenProperty] = property, [HelixDbNativeTokens.TokenUnique] = true }) });
    internal static JsonObject DropIndex(string label, string property, bool range)
    {
        var fields = Index(label, property, range)[HelixDbNativeTokens.TokenCreateIndex]!.AsObject();
        return HelixDbProtocol.Node(HelixDbNativeTokens.TokenDropIndex,
            new() { [HelixDbNativeTokens.TokenSpec] = fields[HelixDbNativeTokens.TokenSpec]!.DeepClone() });
    }

    internal static JsonObject Graph(string label, string id, int depth)
    {
        var branches = new JsonArray();
        for (var hops = SingleResultCardinality; hops <= depth; hops++)
        {
            JsonNode traversal = JsonValue.Create(HelixDbNativeTokens.TokenContext)!;
            for (var hop = EmptyResultCount; hop < hops; hop++)
            {
                traversal = HelixDbProtocol.Node(HelixDbNativeTokens.TokenOut, new() { [HelixDbNativeTokens.TokenInput] = traversal, [HelixDbNativeTokens.TokenLabel] = label + HelixDbNativeTokens.TokenLinks });
            }

            branches.Add(new JsonObject { [HelixDbNativeTokens.TokenRoot] = traversal });
        }

        var union = HelixDbProtocol.Node(HelixDbNativeTokens.TokenUnion, new() { [HelixDbNativeTokens.TokenInput] = Lookup(label, id), [HelixDbNativeTokens.TokenTraversals] = branches });
        var distinct = HelixDbProtocol.Node(HelixDbNativeTokens.TokenDedup, new() { [HelixDbNativeTokens.TokenInput] = union });
        var filtered = HelixDbProtocol.Node(HelixDbNativeTokens.TokenWhere, new() { [HelixDbNativeTokens.TokenInput] = distinct, [HelixDbNativeTokens.TokenPredicate] = HelixDbProtocol.Compare(HelixDbNativeTokens.TokenNeq, HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenString, id) });
        return HelixDbProtocol.Project(HelixDbProtocol.Ordered(filtered, HelixDbNativeTokens.TokenId), HelixDbNativeTokens.TokenId);
    }
}

using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;

internal static class HelixDbDocumentAst
{
    internal static JsonObject Lookup(string label, string id) => HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbTokens.TokenEq, HelixDbTokens.TokenId, HelixDbTokens.TokenString, id));
    internal static JsonObject Read(string label, string id) => HelixDbProtocol.Project(Lookup(label, id), HelixDbTokens.TokenId, HelixDbTokens.TokenPayload);
    internal static JsonObject Add(string label, BenchmarkDocument document) => HelixDbProtocol.Node(HelixDbTokens.TokenAddN, new() { [HelixDbTokens.TokenLabel] = label, [HelixDbTokens.TokenProperties] = new JsonArray { new JsonArray(HelixDbTokens.TokenId, HelixDbProtocol.Value(HelixDbTokens.TokenString, document.Id)), new JsonArray(HelixDbTokens.TokenNumber, HelixDbProtocol.Value(HelixDbTokens.TokenI64, document.Number)), new JsonArray(HelixDbTokens.TokenPayload, HelixDbProtocol.Value(HelixDbTokens.TokenString, document.Json)) } });
    internal static JsonObject Update(string label, BenchmarkDocument document) => HelixDbProtocol.Node(HelixDbTokens.TokenSetProperty, new() { [HelixDbTokens.TokenInput] = Lookup(label, document.Id), [HelixDbTokens.TokenName] = HelixDbTokens.TokenPayload, [HelixDbTokens.TokenValue] = HelixDbProtocol.Value(HelixDbTokens.TokenString, document.Json) });
    internal static JsonObject Page(string label, int after, int capacity) => HelixDbProtocol.Project(HelixDbProtocol.Limit(HelixDbProtocol.Ordered(HelixDbProtocol.Nodes(label, HelixDbProtocol.Compare(HelixDbTokens.TokenGt, HelixDbTokens.TokenNumber, HelixDbTokens.TokenI64, after)), HelixDbTokens.TokenNumber), capacity), HelixDbTokens.TokenId, HelixDbTokens.TokenNumber, HelixDbTokens.TokenPayload);
    internal static JsonObject Index(string label, string property, bool range, bool unique = true) => HelixDbProtocol.Node(HelixDbTokens.TokenCreateIndex, new() { [HelixDbTokens.TokenIfNotExists] = false, [HelixDbTokens.TokenSpec] = HelixDbProtocol.Node(range ? HelixDbTokens.TokenNodeRange : HelixDbTokens.TokenNodeEquality, range ? new() { [HelixDbTokens.TokenLabel] = label, [HelixDbTokens.TokenProperty] = property, [HelixDbTokens.TokenDirection] = HelixDbTokens.TokenAsc } : new() { [HelixDbTokens.TokenLabel] = label, [HelixDbTokens.TokenProperty] = property, [HelixDbTokens.TokenUnique] = unique }) });
    internal static JsonObject DropIndex(string label, string property, bool range, bool unique = true)
    {
        var fields = Index(label, property, range, unique)[HelixDbTokens.TokenCreateIndex]!.AsObject();
        return HelixDbProtocol.Node(HelixDbTokens.TokenDropIndex,
            new()
            {
                [HelixDbTokens.TokenSpec] = fields[HelixDbTokens.TokenSpec]!.DeepClone()
            });
    }

    internal static JsonObject Graph(string label, string id, int depth)
    {
        var branches = new JsonArray();
        for (var hops = 1; hops <= depth; hops++)
        {
            JsonNode traversal = JsonValue.Create(HelixDbTokens.TokenContext)!;
            for (var hop = 0; hop < hops; hop++)
            {
                traversal = HelixDbProtocol.Node(HelixDbTokens.TokenOut, new()
                {
                    [HelixDbTokens.TokenInput] = traversal,
                    [HelixDbTokens.TokenLabel] = label + HelixDbTokens.TokenLinks
                });
            }

            branches.Add(new JsonObject { [HelixDbTokens.TokenRoot] = traversal });
        }

        var union = HelixDbProtocol.Node(HelixDbTokens.TokenUnion, new()
        {
            [HelixDbTokens.TokenInput] = Lookup(label, id),
            [HelixDbTokens.TokenTraversals] = branches
        });
        var distinct = HelixDbProtocol.Node(HelixDbTokens.TokenDedup, new()
        {
            [HelixDbTokens.TokenInput] = union
        });
        var filtered = HelixDbProtocol.Node(HelixDbTokens.TokenWhere, new()
        {
            [HelixDbTokens.TokenInput] = distinct,
            [HelixDbTokens.TokenPredicate] = HelixDbProtocol.Compare(HelixDbTokens.TokenNeq, HelixDbTokens.TokenId, HelixDbTokens.TokenString, id)
        });
        return HelixDbProtocol.Project(HelixDbProtocol.Ordered(filtered, HelixDbTokens.TokenId), HelixDbTokens.TokenId);
    }
}

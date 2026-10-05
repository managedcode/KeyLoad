using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Frames bounded native operation trees and requires durable write acknowledgements.</summary>
internal static class HelixDbProtocol
{
    private const string HelixDbRequestFailed = "HelixDbRequestFailed:";
    internal static async Task<JsonDocument> QueryAsync(HttpClient http, JsonObject root, bool write, NativeComparisonExecutionOptions policy, CancellationToken token)
    {
        using var operationDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        operationDeadline.CancelAfter(policy.OperationTimeout);
        token = operationDeadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, HelixDbNativeTokens.TokenV2Query)
        {
            Content = JsonContent.Create(root)
        };
        if (write)
        {
            request.Headers.Add(HelixDbNativeTokens.TokenXHelixAwaitDurable, HelixDbNativeTokens.TokenTrue);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ComparisonFailureException(HelixDbRequestFailed + (int)response.StatusCode);
        }

        var document = await NativeComparisonResponse.ReadJsonAsync(response.Content, policy, token).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.TryGetProperty(HelixDbNativeTokens.TokenError, out _))
        {
            document.Dispose();
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbInvalidResponse);
        }

        return document;
    }

    internal static JsonElement Rows(JsonDocument response, string name = HelixDbNativeTokens.TokenRows)
    {
        var rows = response.RootElement.GetProperty(name);
        if (rows.ValueKind != JsonValueKind.Array)
        {
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbInvalidRows);
        }

        return rows;
    }

    internal static JsonObject Batch(JsonObject root, bool write, string name = HelixDbNativeTokens.TokenRows) => Batch([Entry(root, name)], write, [name]);
    internal static JsonObject Batch(JsonArray entries, bool write, JsonArray returns)
    {
        var kind = write ? HelixDbNativeTokens.TokenWrite : HelixDbNativeTokens.TokenRead;
        return new()
        {
            [HelixDbNativeTokens.TokenRequestType] = kind,
            [HelixDbNativeTokens.TokenSearchConsistency] = HelixDbNativeTokens.TokenStrong,
            [HelixDbNativeTokens.TokenQuery] = new JsonObject
            {
                [kind] = new JsonObject
                {
                    [HelixDbNativeTokens.TokenEntries] = entries,
                    [HelixDbNativeTokens.TokenReturns] = returns
                }
            }
        };
    }

    internal static JsonObject Entry(JsonObject root, string name) => new()
    {
        [HelixDbNativeTokens.TokenQuery] = new JsonObject
        {
            [HelixDbNativeTokens.TokenName] = name,
            [HelixDbNativeTokens.TokenRoot] = root
        }
    };
    internal static JsonObject Node(string operation, JsonObject fields) => new()
    {
        [operation] = fields
    };
    internal static JsonObject Value(string kind, JsonNode? value) => new()
    {
        [HelixDbNativeTokens.TokenValue] = new JsonObject
        {
            [kind] = value
        }
    };
    internal static JsonObject Constant(string kind, JsonNode? value) => new()
    {
        [HelixDbNativeTokens.TokenConstant] = new JsonObject
        {
            [kind] = value
        }
    };
    internal static JsonObject Compare(string operation, string property, string kind, JsonNode? value) => Node(operation, new() { [HelixDbNativeTokens.TokenLeft] = new JsonObject { [HelixDbNativeTokens.TokenProperty] = property }, [HelixDbNativeTokens.TokenRight] = Constant(kind, value) });
    internal static JsonObject And(params JsonObject[] predicates) => Node(HelixDbNativeTokens.TokenAnd, new() { [HelixDbNativeTokens.TokenPredicates] = new JsonArray(predicates.Select(p => (JsonNode)p).ToArray()) });
    internal static JsonObject Nodes(string label, JsonObject? predicate = null) => Node(HelixDbNativeTokens.TokenNodesWhere, new() { [HelixDbNativeTokens.TokenPredicate] = predicate is null ? Compare(HelixDbNativeTokens.TokenEq, HelixDbNativeTokens.MetaTokenLabel, HelixDbNativeTokens.TokenString, label) : And(Compare(HelixDbNativeTokens.TokenEq, HelixDbNativeTokens.MetaTokenLabel, HelixDbNativeTokens.TokenString, label), predicate) });
    internal static JsonObject Project(JsonObject input, params string[] properties) => Node(HelixDbNativeTokens.TokenValueMap, new() { [HelixDbNativeTokens.TokenInput] = input, [HelixDbNativeTokens.TokenProperties] = new JsonArray(properties.Select(p => (JsonNode? )JsonValue.Create(p)).ToArray()) });
    internal static JsonObject Limit(JsonObject input, int count) => Node(HelixDbNativeTokens.TokenLimit, new() { [HelixDbNativeTokens.TokenInput] = input, [HelixDbNativeTokens.TokenCount] = new JsonObject { [HelixDbNativeTokens.TokenLiteral] = count } });
    internal static JsonObject Ordered(JsonObject input, string property) => Node(HelixDbNativeTokens.TokenOrderBy, new() { [HelixDbNativeTokens.TokenInput] = input, [HelixDbNativeTokens.TokenProperty] = property, [HelixDbNativeTokens.TokenOrder] = HelixDbNativeTokens.TokenAsc });
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Frames bounded execution operation trees and requires durable write acknowledgements.</summary>
internal static class HelixDbProtocol
{
    private const string HelixDbRequestFailed = "HelixDbRequestFailed:";
    private const string UniqueConstraintViolation = "unique_constraint_violation";
    internal static async Task<JsonDocument> QueryAsync(HttpClient http, JsonObject root, bool write, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token, bool create = false)
    {
        using var operationDeadline = new ComparisonCancellationSource(timeProvider, token);
        operationDeadline.CancelAfter(policy.OperationTimeout);
        token = operationDeadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, HelixDbTokens.TokenV2Query)
        {
            Content = JsonContent.Create(root)
        };
        if (write)
        {
            request.Headers.Add(HelixDbTokens.TokenXHelixAwaitDurable, HelixDbTokens.TokenTrue);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using var error = await ComparisonResponse.ReadJsonAsync(response.Content, policy, token).ConfigureAwait(false);
            if (create && error.RootElement.ValueKind == JsonValueKind.Object
                && error.RootElement.TryGetProperty(HelixDbTokens.TokenError, out var code)
                && code.ValueKind == JsonValueKind.String && code.GetString() == UniqueConstraintViolation)
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict);
            }
            throw new ComparisonFailureException(HelixDbRequestFailed + (int)response.StatusCode);
        }

        var document = await ComparisonResponse.ReadJsonAsync(response.Content, policy, token).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.TryGetProperty(HelixDbTokens.TokenError, out _))
        {
            document.Dispose();
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbInvalidResponse);
        }

        return document;
    }

    internal static JsonElement Rows(JsonDocument response, string name = HelixDbTokens.TokenRows)
    {
        var rows = response.RootElement.GetProperty(name);
        if (rows.ValueKind != JsonValueKind.Array)
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbInvalidRows);
        }

        return rows;
    }

    internal static JsonObject Batch(JsonObject root, bool write, string name = HelixDbTokens.TokenRows) => Batch([Entry(root, name)], write, [name]);
    internal static JsonObject Batch(JsonArray entries, bool write, JsonArray returns)
    {
        var kind = write ? HelixDbTokens.TokenWrite : HelixDbTokens.TokenRead;
        return new()
        {
            [HelixDbTokens.TokenRequestType] = kind,
            [HelixDbTokens.TokenSearchConsistency] = HelixDbTokens.TokenStrong,
            [HelixDbTokens.TokenQuery] = new JsonObject
            {
                [kind] = new JsonObject
                {
                    [HelixDbTokens.TokenEntries] = entries,
                    [HelixDbTokens.TokenReturns] = returns
                }
            }
        };
    }

    internal static JsonObject Entry(JsonObject root, string name) => new()
    {
        [HelixDbTokens.TokenQuery] = new JsonObject
        {
            [HelixDbTokens.TokenName] = name,
            [HelixDbTokens.TokenRoot] = root
        }
    };
    internal static JsonObject Node(string operation, JsonObject fields) => new()
    {
        [operation] = fields
    };
    internal static JsonObject Value(string kind, JsonNode? value) => new()
    {
        [HelixDbTokens.TokenValue] = new JsonObject
        {
            [kind] = value
        }
    };
    internal static JsonObject Constant(string kind, JsonNode? value) => new()
    {
        [HelixDbTokens.TokenConstant] = new JsonObject
        {
            [kind] = value
        }
    };
    internal static JsonObject Compare(string operation, string property, string kind, JsonNode? value) => Node(operation, new() { [HelixDbTokens.TokenLeft] = new JsonObject { [HelixDbTokens.TokenProperty] = property }, [HelixDbTokens.TokenRight] = Constant(kind, value) });
    internal static JsonObject And(params JsonObject[] predicates) => Node(HelixDbTokens.TokenAnd, new() { [HelixDbTokens.TokenPredicates] = new JsonArray(predicates.Select(p => (JsonNode)p).ToArray()) });
    internal static JsonObject Nodes(string label, JsonObject? predicate = null) => Node(HelixDbTokens.TokenNodesWhere, new() { [HelixDbTokens.TokenPredicate] = predicate is null ? Compare(HelixDbTokens.TokenEq, HelixDbTokens.MetaTokenLabel, HelixDbTokens.TokenString, label) : And(Compare(HelixDbTokens.TokenEq, HelixDbTokens.MetaTokenLabel, HelixDbTokens.TokenString, label), predicate) });
    internal static JsonObject Project(JsonObject input, params string[] properties) => Node(HelixDbTokens.TokenValueMap, new() { [HelixDbTokens.TokenInput] = input, [HelixDbTokens.TokenProperties] = new JsonArray(properties.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) });
    internal static JsonObject Limit(JsonObject input, int count) => Node(HelixDbTokens.TokenLimit, new() { [HelixDbTokens.TokenInput] = input, [HelixDbTokens.TokenCount] = new JsonObject { [HelixDbTokens.TokenLiteral] = count } });
    internal static JsonObject Ordered(JsonObject input, string property) => Node(HelixDbTokens.TokenOrderBy, new() { [HelixDbTokens.TokenInput] = input, [HelixDbTokens.TokenProperty] = property, [HelixDbTokens.TokenOrder] = HelixDbTokens.TokenAsc });
}

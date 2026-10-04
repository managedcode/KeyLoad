using System.Text.Json.Nodes;

namespace KeyLoad.Server;

/// <summary>Closed wire schema of a freshly reconstructed safe Errors.Problem, matching its owning converter.</summary>
internal static class McpSafeProblemSchema
{
    internal static JsonObject Create()
    {
        var properties = new JsonObject
        {
            [McpSchemaKeywords.ProblemType] = String(),
            [McpSchemaKeywords.ProblemTitle] = String(),
            [McpSchemaKeywords.ProblemStatus] = new JsonObject
            {
                [McpSchemaKeywords.Type] = McpSchemaKeywords.Integer,
                [McpSchemaKeywords.Enum] = new JsonArray(Enum.GetValues<ErrorCode>().Select(Errors.Status).Distinct()
                    .Select(status => (JsonNode?)JsonValue.Create(status)).ToArray())
            },
            [McpSchemaKeywords.ProblemDetail] = String(),
            [McpSchemaKeywords.ProblemCode] = new JsonObject
            {
                [McpSchemaKeywords.Type] = McpSchemaKeywords.String,
                [McpSchemaKeywords.Enum] = new JsonArray(Enum.GetNames<ErrorCode>()
                    .Select(code => (JsonNode?)JsonValue.Create(code)).ToArray())
            }
        };
        return McpSchemaObjects.Closed(properties, new JsonArray(
            McpSchemaKeywords.ProblemType, McpSchemaKeywords.ProblemTitle, McpSchemaKeywords.ProblemStatus,
            McpSchemaKeywords.ProblemDetail, McpSchemaKeywords.ProblemCode));
    }

    private static JsonObject String() => new() { [McpSchemaKeywords.Type] = McpSchemaKeywords.String };
}

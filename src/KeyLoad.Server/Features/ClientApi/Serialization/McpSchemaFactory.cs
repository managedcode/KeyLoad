using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace KeyLoad.Server;

/// <summary>Native canonical metadata projection and exact version-one wrapper schemas.</summary>
internal static class McpSchemaFactory
{
    private static readonly JsonSerializerOptions Projection = McpSchemaProjection.Create();
    private static readonly JsonSchemaExporterOptions InputOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = McpSchemaTransform.Input
    };
    private static readonly JsonSchemaExporterOptions OutputOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = McpSchemaTransform.Output
    };

    /// <summary>Describes exact outer arguments with a single complete canonical DTO graph.</summary>
    /// <param name="requestType">Actual typed request; absent only for no-body capabilities.</param>
    /// <param name="outerCommandId">Whether the existing HTTP command identity belongs to the outer arguments.</param>
    /// <returns>An owned immutable root-object schema.</returns>
    internal static JsonElement CreateInput(Type? requestType, bool outerCommandId)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var root = McpSchemaObjects.Closed(properties, required);
        root[McpSchemaKeywords.Schema] = McpSchemaKeywords.SchemaVersion;
        if (requestType is null)
        {
            if (outerCommandId)
            { throw new ArgumentException(McpCatalogProtocol.InvalidOperation, nameof(outerCommandId)); }
            return JsonSerializer.SerializeToElement(root);
        }
        var request = Export(requestType, InputOptions, McpSchemaKeywords.RequestReference);
        root[McpSchemaKeywords.Definitions] = new JsonObject { [McpCatalogProtocol.Request] = request };
        properties[McpCatalogProtocol.Request] = McpSchemaObjects.Reference(McpSchemaKeywords.RequestReference);
        required.Add(McpCatalogProtocol.Request);
        if (outerCommandId)
        {
            properties[McpCatalogProtocol.CommandId] = McpSchemaObjects.Identity(false);
            required.Add(McpCatalogProtocol.CommandId);
        }
        return JsonSerializer.SerializeToElement(root);
    }

    /// <summary>Describes disjoint canonical success and safe error envelopes.</summary>
    /// <param name="resultType">Actual typed canonical operation result.</param>
    /// <param name="nullableResult">Explicit root nullability from the frozen operation contract.</param>
    /// <returns>An owned immutable structured-output schema.</returns>
    internal static JsonElement CreateOutput(Type resultType, bool nullableResult)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        var success = McpSchemaObjects.Closed(new JsonObject
        {
            [McpCatalogProtocol.Result] = nullableResult ? McpSchemaObjects.NullableResult() :
                McpSchemaObjects.Reference(McpSchemaKeywords.ResultReference),
            [McpCatalogProtocol.RequestId] = McpSchemaObjects.Identity(false)
        }, new JsonArray(McpCatalogProtocol.Result, McpCatalogProtocol.RequestId));
        var failure = McpSchemaObjects.Closed(new JsonObject
        {
            [McpCatalogProtocol.Error] = McpSchemaObjects.Reference(McpSchemaKeywords.ErrorReference),
            [McpCatalogProtocol.RequestId] = McpSchemaObjects.Identity(true)
        }, new JsonArray(McpCatalogProtocol.Error, McpCatalogProtocol.RequestId));
        var root = new JsonObject
        {
            [McpSchemaKeywords.Schema] = McpSchemaKeywords.SchemaVersion,
            [McpSchemaKeywords.Type] = McpSchemaKeywords.Object,
            [McpSchemaKeywords.OneOf] = new JsonArray(success, failure),
            [McpSchemaKeywords.UnevaluatedProperties] = false,
            [McpSchemaKeywords.Definitions] = new JsonObject
            {
                [McpCatalogProtocol.Result] = Export(resultType, OutputOptions, McpSchemaKeywords.ResultReference),
                [McpCatalogProtocol.Error] = McpSafeProblemSchema.Create()
            }
        };
        return JsonSerializer.SerializeToElement(root);
    }

    private static JsonNode Export(Type type, JsonSchemaExporterOptions options, string prefix)
    {
        var schema = Projection.GetJsonSchemaAsNode(type, options);
        McpSchemaReferences.Rebase(schema, prefix);
        return schema;
    }
}

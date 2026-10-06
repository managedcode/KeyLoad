using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Core.Features.RelationalStorage;

/// <summary>Enforces persisted typed-row schemas inside the existing canonical mutation boundary.</summary>
public static class RelationalRowValidation
{
    private const string InvalidRow = "A relational row must be a closed object with unique declared columns.";
    private const string MissingColumn = "A required relational column is missing.";
    private const string InvalidPrimaryKey = "The relational primary key must match the canonical entity identifier.";
    private const string InvalidPatch = "A relational patch must address a declared top-level column.";
    private const string InvalidJson = "The relational value is invalid JSON.";
    private const string PayloadLimitDetail = "The relational value exceeds its byte limit.";
    private const string NullJson = "null";

    /// <summary>Validates a new resource's optional typed-row schema and native index bindings.</summary>
    /// <param name="definition">Resource definition to validate before catalog publication.</param>
    public static void ValidateSchema(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.RelationalSchema is null)
        {
            return;
        }
        RelationalSchemaRules.Indexes(definition, RelationalSchemaRules.Columns(definition));
    }

    /// <summary>Validates the exact final row image before canonicalization or native index writes.</summary>
    /// <param name="definition">Persisted authoritative resource schema.</param>
    /// <param name="id">Canonical entity identifier required to equal the primary-key value.</param>
    /// <param name="json">Original final-image JSON, preserving numeric precision.</param>
    /// <param name="limits">Configured original JSON byte and nesting bounds; the snapshot belongs to the calling database owner.</param>
    public static void ValidateRow(ResourceDefinition definition, string id, string json, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.RelationalSchema is null)
        {
            return;
        }
        ArgumentNullException.ThrowIfNull(limits);
        JsonData.Identifier(id);
        var columns = RelationalSchemaRules.Columns(definition);
        using var document = Parse(json, limits);
        var seen = ValidateProperties(document.RootElement, columns);
        foreach (var column in columns.Values)
        {
            if (!column.Nullable && !seen.Contains(column.Name))
            {
                throw Errors.Fail(ErrorCode.Validation, MissingColumn);
            }
        }
        if (!StringComparer.Ordinal.Equals(document.RootElement.GetProperty(definition.RelationalSchema.PrimaryKey).GetString(), id))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPrimaryKey);
        }
    }

    /// <summary>Checks raw patch scalar values before ordinary patch canonicalization can round numbers.</summary>
    /// <param name="definition">Persisted authoritative resource schema.</param>
    /// <param name="patches">Bounded field changes; final required-column and primary-key checks follow separately.</param>
    /// <param name="limits">Configured original scalar JSON byte and nesting bounds; the snapshot belongs to the calling database owner.</param>
    public static void ValidatePatchValues(ResourceDefinition definition, ImmutableArray<FieldPatch> patches, DatabaseLimits limits)
    {
        const int ColumnPathSegments = 1;
        const int ColumnPathIndex = 0;

        ArgumentNullException.ThrowIfNull(definition);
        if (definition.RelationalSchema is null)
        {
            return;
        }
        if (patches.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPatch);
        }
        var columns = RelationalSchemaRules.Columns(definition);
        ArgumentNullException.ThrowIfNull(limits);
        var selectedLimits = limits;
        foreach (var patch in patches)
        {
            if (patch is null)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidPatch);
            }
            var path = JsonData.PathSegments(patch.Path);
            if (path.Length != ColumnPathSegments || !columns.TryGetValue(path[ColumnPathIndex], out var column) || !Enum.IsDefined(patch.Kind))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidPatch);
            }
            if (patch.Kind == PatchKind.Set)
            {
                using var value = Parse(patch.ValueJson ?? NullJson, selectedLimits);
                RelationalScalarValidation.Require(column, value.RootElement);
            }
        }
    }

    private static HashSet<string> ValidateProperties(JsonElement row, Dictionary<string, RelationalColumn> columns)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRow);
        }
        var seen = new HashSet<string>(columns.Count, StringComparer.Ordinal);
        foreach (var property in row.EnumerateObject())
        {
            if (!columns.TryGetValue(property.Name, out var column) || !seen.Add(property.Name))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRow);
            }
            RelationalScalarValidation.Require(column, property.Value);
        }
        return seen;
    }

    private static JsonDocument Parse(string json, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > limits.MaxDocumentBytes || Encoding.UTF8.GetByteCount(json) > limits.MaxDocumentBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PayloadLimitDetail);
        }
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidJson);
        }
    }
}

namespace KeyLoad.Core.Features.RelationalStorage;

internal static class RelationalSchemaRules
{
    private const int MaximumColumns = 256;
    private const string InvalidSchema = "The relational schema is invalid.";
    private const string SchemaBudgetExceeded = "The relational schema exceeds its column budget.";
    private const string InvalidIndex = "A relational index must refer to declared top-level columns.";
    private const string RevisionColumn = "revision";
    private const string WildcardColumn = "*";
    private const string IdentifierColumn = "id";
    private const char MetadataPrefix = '@';

    internal static Dictionary<string, RelationalColumn> Columns(ResourceDefinition definition)
    {
        var schema = definition.RelationalSchema!;
        if (definition.Kind != ResourceKind.Collection || definition.Authority != DocumentAuthority.Document
            || schema.Columns.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidSchema);
        }
        if (schema.Columns.Length > MaximumColumns)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, SchemaBudgetExceeded);
        }
        JsonData.Identifier(schema.PrimaryKey);
        var columns = new Dictionary<string, RelationalColumn>(schema.Columns.Length, StringComparer.Ordinal);
        foreach (var column in schema.Columns)
        {
            if (column is null || !Enum.IsDefined(column.Type))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidSchema);
            }
            JsonData.Identifier(column.Name);
            if (IsReserved(column.Name, schema.PrimaryKey) || !columns.TryAdd(column.Name, column))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidSchema);
            }
        }
        if (!columns.TryGetValue(schema.PrimaryKey, out var primary) || primary.Nullable || primary.Type != RelationalColumnType.Text)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidSchema);
        }
        return columns;
    }

    private static bool IsReserved(string name, string primaryKey)
        => name is RevisionColumn or WildcardColumn || name.StartsWith(MetadataPrefix)
            || name == IdentifierColumn && primaryKey != IdentifierColumn;

    internal static void Indexes(ResourceDefinition definition, Dictionary<string, RelationalColumn> columns)
    {
        if (definition.Indexes.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidIndex);
        }
        foreach (var index in definition.Indexes)
        {
            if (index is null || index.Fields.IsDefaultOrEmpty)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidIndex);
            }
            foreach (var field in index.Fields)
            {
                if (string.IsNullOrEmpty(field))
                {
                    throw Errors.Fail(ErrorCode.Validation, InvalidIndex);
                }
                var path = JsonData.PathSegments(field);
                if (path.Length != 1 || !columns.ContainsKey(path[0]))
                {
                    throw Errors.Fail(ErrorCode.Validation, InvalidIndex);
                }
            }
        }
    }
}

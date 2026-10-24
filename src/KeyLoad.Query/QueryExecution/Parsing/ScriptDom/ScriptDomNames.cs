using KeyLoad.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.QueryExecution;

internal static class ScriptDomNames
{
    internal static List<string> Column(Dom.ColumnReferenceExpression column)
    {
        if (column.Collation is not null || column.ColumnType != Dom.ColumnType.Regular || column.MultiPartIdentifier is null
            || column.MultiPartIdentifier.Identifiers.Count == EmptyCount)
        {
            throw ScriptDomErrors.Unsupported();
        }
        var parts = column.MultiPartIdentifier.Identifiers.Select(identifier => identifier.Value).ToList();
        foreach (var part in parts)
        {
            global::KeyLoad.Core.JsonData.Identifier(part);
        }
        return parts;
    }
    internal static string Path(List<string> parts, string? alias, bool documentMetadata)
    {
        if (alias is not null && parts.Count > SingleElement && parts[FirstIndex] == alias)
        {
            parts.RemoveAt(FirstIndex);
        }
        if (documentMetadata && parts.Count == SingleElement
            && parts[FirstIndex] is SqlSyntax.MetadataId or SqlSyntax.MetadataRevision)
        {
            return SqlSyntax.MetadataPrefix + parts[FirstIndex];
        }
        return global::KeyLoad.Core.JsonData.Path(parts.ToArray());
    }
    internal static string Object(Dom.SchemaObjectName name)
    {
        if (name.Identifiers.Count != SingleElement)
        {
            throw ScriptDomErrors.Unsupported();
        }
        var value = name.Identifiers[FirstIndex].Value;
        global::KeyLoad.Core.JsonData.Identifier(value);
        return value;
    }

    private const int FirstIndex = 0;
    private const int SingleElement = 1;
    private const int EmptyCount = 0;
}

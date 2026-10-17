using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomCreateTableShape
{
    private const int EmptyCount = 0;
    internal static void Require(Dom.CreateTableStatement table, ScriptDomTableLoweringContext context)
    {
        context.Check();
        if (table.AsEdge || table.AsNode || table.AsFileTable || table.CloneSource is not null
            || table.ClonePointInTime is not null || table.CtasColumns.Count != EmptyCount
            || table.FederationScheme is not null || table.FileStreamOn is not null
            || table.OnFileGroupOrPartitionScheme is not null || table.TextImageOn is not null
            || table.Options.Count != EmptyCount || table.SelectStatement is not null
            || table.Definition is null || table.Definition.Indexes.Count != EmptyCount
            || table.Definition.SystemTimePeriod is not null || table.Definition.TableConstraints.Count != EmptyCount)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
    }
    internal static void Require(Dom.ColumnDefinition column, ScriptDomTableLoweringContext context)
    {
        context.Check();
        if (column.Collation is not null || column.ComputedColumnExpression is not null
            || column.DefaultConstraint is not null || column.Encryption is not null
            || column.GeneratedAlways is not null || column.IdentityOptions is not null
            || column.Index is not null || column.IsHidden || column.IsMasked || column.IsPersisted
            || column.IsRowGuidCol || column.MaskingFunction is not null || column.StorageOptions is not null)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
    }
}

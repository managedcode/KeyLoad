using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomTableColumnConstraints
{
    private const int EmptyCount = 0;
    internal static (bool Nullable, bool Primary) Read(Dom.ColumnDefinition column, ScriptDomTableLoweringContext context)
    {
        bool? nullable = null;
        var primary = false;
        foreach (var constraint in column.Constraints)
        {
            context.Check();
            if (constraint.ConstraintIdentifier is not null)
            {
                throw ScriptDomOperationErrors.Unsupported();
            }
            if (constraint is Dom.NullableConstraintDefinition nullability)
            {
                if (nullable.HasValue)
                {
                    throw ScriptDomOperationErrors.Invalid();
                }
                nullable = nullability.Nullable;
            }
            else if (constraint is Dom.UniqueConstraintDefinition unique && unique.IsPrimaryKey)
            {
                RequirePrimaryShape(unique, primary);
                primary = true;
            }
            else
            {
                throw ScriptDomOperationErrors.Unsupported();
            }
        }
        if (nullable is null && !primary || nullable == true && primary)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        return (nullable ?? false, primary);
    }
    private static void RequirePrimaryShape(Dom.UniqueConstraintDefinition unique, bool primary)
    {
        if (primary)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        if (unique.Clustered is not null || unique.Columns.Count != EmptyCount
            || unique.FileStreamOn is not null || unique.IndexOptions.Count != EmptyCount
            || unique.IndexType?.IndexTypeKind is not null || unique.IsEnforced == false
            || unique.OnFileGroupOrPartitionScheme is not null)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
    }

}

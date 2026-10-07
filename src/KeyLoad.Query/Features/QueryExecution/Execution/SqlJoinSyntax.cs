using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

/// <summary>Reads the closed Q2 join syntax and binds source-qualified fields.</summary>
internal sealed class SqlJoinSyntax(SqlTokenCursor cursor, int dialectVersion)
{
    private const int JoinDialectVersion = 2;
    private const int FirstElementIndex = 0;
    private const int SinglePathSegment = 1;
    private const int QualifiedPathSegmentCount = 2;

    internal InnerJoinClause? ReadJoin(string? leftAlias)
    {
        if (!cursor.Eat(SqlSyntax.Inner))
        {
            return null;
        }
        if (dialectVersion != JoinDialectVersion)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedJoinDetail);
        }
        cursor.Need(SqlSyntax.Join);
        var collection = cursor.Identifier();
        cursor.Eat(SqlSyntax.As);
        var rightAlias = cursor.Identifier();
        if (leftAlias is null || StringComparer.Ordinal.Equals(leftAlias, rightAlias))
        {
            throw SqlSyntax.Invalid();
        }
        cursor.Need(SqlSyntax.On);
        var leftKey = SourceKeyPath(leftAlias);
        cursor.Need(SqlSyntax.Equals);
        var rightKey = SourceKeyPath(rightAlias);
        if (!StringComparer.Ordinal.Equals(leftKey.SourceAlias, leftAlias)
            || !StringComparer.Ordinal.Equals(rightKey.SourceAlias, rightAlias))
        {
            throw SqlSyntax.Invalid();
        }
        return new(collection, rightAlias, leftKey.Path, rightKey.Path);
    }

    internal static Selection BindProjection(Selection selection, string? leftAlias, InnerJoinClause join)
    {
        var parts = JsonData.PathSegments(selection.Path);
        if (parts.Length < QualifiedPathSegmentCount)
        {
            throw SqlSyntax.Invalid();
        }
        var source = parts[FirstElementIndex];
        if (source != leftAlias && source != join.Alias)
        {
            throw SqlSyntax.Invalid();
        }
        var path = JsonData.Path(parts.Skip(SinglePathSegment).ToArray());
        return selection with { Path = path, SourceAlias = source };
    }

    internal static string BoundOrderPath(List<string> parts, string? leftAlias)
    {
        if (parts.Count != QualifiedPathSegmentCount || !StringComparer.Ordinal.Equals(parts[FirstElementIndex], leftAlias))
        {
            throw SqlSyntax.Invalid();
        }
        return JsonData.Path([parts[FirstElementIndex + SinglePathSegment]]);
    }

    internal static string BoundPath(string path, string? alias)
    {
        if (path == SqlSyntax.Star)
        {
            return path;
        }
        var parts = JsonData.PathSegments(path).ToList();
        if (alias is not null && parts.Count > SinglePathSegment && parts[FirstElementIndex] == alias)
        {
            parts.RemoveAt(FirstElementIndex);
        }
        return parts.Count == SinglePathSegment
            && parts[FirstElementIndex] is SqlSyntax.MetadataId or SqlSyntax.MetadataRevision
            ? SqlSyntax.MetadataPrefix + parts[FirstElementIndex] : JsonData.Path(parts.ToArray());
    }

    private (string SourceAlias, string Path) SourceKeyPath(string expectedAlias)
    {
        var parts = cursor.Path();
        if (parts.Count != QualifiedPathSegmentCount || !StringComparer.Ordinal.Equals(parts[FirstElementIndex], expectedAlias))
        {
            throw SqlSyntax.Invalid();
        }
        return (parts[FirstElementIndex], JsonData.Path([parts[parts.Count - SinglePathSegment]]));
    }
}

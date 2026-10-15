using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Requests
{
    private const string SourceAlias = "d";
    private const string Equal = "=";
    private const string SqlPrefix = "SELECT ";
    private const string SqlFrom = " FROM [";
    private const string SqlWhere = "] d WHERE d.id = 'a' ORDER BY d.id";
    private const string SqlStar = "*";
    private const string SqlProtected = "d.[odd.name] AS exposed";
    private const string SqlPublicAlias = "d.status AS [odd.name]";
    private const string SqlNull = "d.publicNull AS exposed";
    private const string PredicatePrefix = "] d WHERE d.[odd.name] = '";
    private const string PredicateSuffix = "' ORDER BY d.id";
    private const string OrderSuffix = "] d WHERE d.id = 'a' ORDER BY d.[odd.name]";

    internal static (QueryRequest Sql, AstQueryRequest Ast) Selection(
        SafeProjectorOperationRf3State state, int selection)
    {
        var (sql, path, alias) = selection switch
        {
            StarSelection => (SqlStar, SafeProjectorOperationRf3Protocol.Star, SafeProjectorOperationRf3Protocol.Star),
            ProtectedSelection => (SqlProtected, SafeProjectorOperationRf3Protocol.LiteralPath, SafeProjectorOperationRf3Protocol.Exposed),
            PublicAliasSelection => (SqlPublicAlias, SafeProjectorOperationRf3Protocol.StatusPath, SafeProjectorOperationRf3Protocol.LiteralAlias),
            NullSelection => (SqlNull, SafeProjectorOperationRf3Protocol.NullPath, SafeProjectorOperationRf3Protocol.Exposed),
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        var predicate = new Comparison(new FieldOperand(SafeProjectorOperationRf3Protocol.IdPath), Equal,
            ValueOperand.Create(SafeProjectorOperationRf3Protocol.First));
        return (new(state.Partition, SqlPrefix + sql + SqlFrom + state.Collection + SqlWhere, AllowFullScan: true),
            new(state.Partition, new(state.Collection, SourceAlias, [new(path, alias)], predicate,
                [new(SafeProjectorOperationRf3Protocol.IdPath, false)], SafeProjectorOperationRf3Protocol.BothRows), AllowFullScan: true));
    }

    internal static QueryRequest UseSql(SafeProjectorOperationRf3State state, bool order)
        => new(state.Partition, SqlPrefix + SqlStar + SqlFrom + state.Collection
            + (order ? OrderSuffix : PredicatePrefix + SafeProjectorOperationRf3Protocol.Canary + PredicateSuffix), AllowFullScan: true);

    internal static AstQueryRequest UseAst(SafeProjectorOperationRf3State state, bool order)
        => new(state.Partition, new(state.Collection, SourceAlias,
            [new(SafeProjectorOperationRf3Protocol.Star, SafeProjectorOperationRf3Protocol.Star)],
            new Comparison(new FieldOperand(order ? SafeProjectorOperationRf3Protocol.IdPath : SafeProjectorOperationRf3Protocol.LiteralPath),
                Equal, ValueOperand.Create(order ? SafeProjectorOperationRf3Protocol.First : SafeProjectorOperationRf3Protocol.Canary)),
            [new(order ? SafeProjectorOperationRf3Protocol.LiteralPath : SafeProjectorOperationRf3Protocol.IdPath, false)],
            SafeProjectorOperationRf3Protocol.BothRows), AllowFullScan: true);

    internal const int StarSelection = 0;
    internal const int ProtectedSelection = 1;
    internal const int PublicAliasSelection = 2;
    internal const int NullSelection = 3;
    internal const int SelectionCount = 4;
}

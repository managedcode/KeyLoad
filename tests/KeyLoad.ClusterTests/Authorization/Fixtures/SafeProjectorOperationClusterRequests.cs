using KeyLoad.Query;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SafeProjectorOperationClusterRequests
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
        SafeProjectorOperationClusterState state, int selection)
    {
        var (sql, path, alias) = selection switch
        {
            StarSelection => (SqlStar, SafeProjectorOperationClusterProtocol.Star, SafeProjectorOperationClusterProtocol.Star),
            ProtectedSelection => (SqlProtected, SafeProjectorOperationClusterProtocol.LiteralPath, SafeProjectorOperationClusterProtocol.Exposed),
            PublicAliasSelection => (SqlPublicAlias, SafeProjectorOperationClusterProtocol.StatusPath, SafeProjectorOperationClusterProtocol.LiteralAlias),
            NullSelection => (SqlNull, SafeProjectorOperationClusterProtocol.NullPath, SafeProjectorOperationClusterProtocol.Exposed),
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        var predicate = new Comparison(new FieldOperand(SafeProjectorOperationClusterProtocol.IdPath), Equal,
            ValueOperand.Create(SafeProjectorOperationClusterProtocol.First));
        return (new(state.Partition, SqlPrefix + sql + SqlFrom + state.Collection + SqlWhere, AllowFullScan: true),
            new(state.Partition, new(state.Collection, SourceAlias, [new(path, alias)], predicate,
                [new(SafeProjectorOperationClusterProtocol.IdPath, false)], SafeProjectorOperationClusterProtocol.BothRows), AllowFullScan: true));
    }

    internal static QueryRequest UseSql(SafeProjectorOperationClusterState state, bool order)
        => new(state.Partition, SqlPrefix + SqlStar + SqlFrom + state.Collection
            + (order ? OrderSuffix : PredicatePrefix + SafeProjectorOperationClusterProtocol.Canary + PredicateSuffix), AllowFullScan: true);

    internal static AstQueryRequest UseAst(SafeProjectorOperationClusterState state, bool order)
        => new(state.Partition, new(state.Collection, SourceAlias,
            [new(SafeProjectorOperationClusterProtocol.Star, SafeProjectorOperationClusterProtocol.Star)],
            new Comparison(new FieldOperand(order ? SafeProjectorOperationClusterProtocol.IdPath : SafeProjectorOperationClusterProtocol.LiteralPath),
                Equal, ValueOperand.Create(order ? SafeProjectorOperationClusterProtocol.First : SafeProjectorOperationClusterProtocol.Canary)),
            [new(order ? SafeProjectorOperationClusterProtocol.LiteralPath : SafeProjectorOperationClusterProtocol.IdPath, false)],
            SafeProjectorOperationClusterProtocol.BothRows), AllowFullScan: true);

    internal const int StarSelection = 0;
    internal const int ProtectedSelection = 1;
    internal const int PublicAliasSelection = 2;
    internal const int NullSelection = 3;
    internal const int SelectionCount = 4;
}

using KeyLoad.Query;
using static KeyLoad.UnitTests.Features.QueryExecution.BinderFieldLineageProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SchemaVisibleProjectionNativeRequests
{
    private const string Alias = "d";
    private const string RightAlias = "r";
    private const string Equal = "=";
    private const string MissingPath = "/doesNotExist";
    private const string IdPath = "/@id";
    private const string RevisionPath = "/@revision";
    private const string PublicSource = "d.status";
    private const string ProtectedSource = "d.[odd.name]";
    private const string MissingSource = "d.doesNotExist";
    private const string Prefix = "SELECT ";
    private const string From = " FROM orders d";
    private const string As = " AS exposed";
    private const string Where = " WHERE ";
    private const string Value = " = 'open'";
    private const string Open = "open";
    private const string Order = " ORDER BY ";
    private const string JoinSql = "SELECT d.status AS exposed FROM orders d INNER JOIN eventcustomers r ON d.status = r.name";
    private const int Limit = 10;
    private const int Q2 = 2;

    internal static IEnumerable<(QueryRequest Sql, AstQueryRequest Ast)> Restricted(PartitionRef partition)
    {
        foreach (var (source, path) in new[]
        {
            (PublicSource, StatusPath), (ProtectedSource, DottedPolicy), (MissingSource, MissingPath)
        })
        {
            yield return Pair(partition, Prefix + source + As + From, [new(path, SchemaVisibleProjectionNativeProtocol.ValueAlias)]);
            yield return Pair(partition, Prefix + SchemaVisibleProjectionNativeProtocol.Star + From + Where + source + Value,
                [new(SchemaVisibleProjectionNativeProtocol.Star, SchemaVisibleProjectionNativeProtocol.Star)],
                new Comparison(new FieldOperand(path), Equal, ValueOperand.Create(Open)));
            yield return Pair(partition, Prefix + SchemaVisibleProjectionNativeProtocol.Star + From + Order + source,
                [new(SchemaVisibleProjectionNativeProtocol.Star, SchemaVisibleProjectionNativeProtocol.Star)], order: [new(path, false)]);
        }
        var join = Star(partition);
        yield return (new(partition, JoinSql, AllowFullScan: true, QueryDialectVersion: Q2), join with
        {
            AstVersion = Q2,
            Query = join.Query with
            {
                Projection = [new(StatusPath, SchemaVisibleProjectionNativeProtocol.ValueAlias, Alias)],
                InnerJoin = new(SqlEventReferenceJoinFixture.Target, RightAlias, StatusPath, "/name")
            }
        });
    }

    internal static AstQueryRequest Star(PartitionRef partition) => new(partition,
        new(Collection, Alias, [new(SchemaVisibleProjectionNativeProtocol.Star, SchemaVisibleProjectionNativeProtocol.Star)],
            null, [], Limit), AllowFullScan: true);

    internal static (QueryRequest Sql, AstQueryRequest Ast) Selected(PartitionRef partition, bool protectedPath)
        => Pair(partition, Prefix + (protectedPath ? ProtectedSource : PublicSource) + As + From,
            [new(protectedPath ? DottedPolicy : StatusPath, SchemaVisibleProjectionNativeProtocol.ValueAlias)]);

    internal static IEnumerable<AstQueryRequest> System(PartitionRef partition)
    {
        yield return Star(partition) with
        {
            Query = Star(partition).Query with
            {
                Projection = [new(IdPath, SchemaVisibleProjectionNativeProtocol.ValueAlias)]
            }
        };
        yield return Star(partition) with
        {
            Query = Star(partition).Query with
            {
                Projection = [new(RevisionPath, SchemaVisibleProjectionNativeProtocol.ValueAlias)]
            }
        };
    }

    private static (QueryRequest Sql, AstQueryRequest Ast) Pair(PartitionRef partition, string sql,
        System.Collections.Immutable.ImmutableArray<Selection> selections, Predicate? predicate = null,
        System.Collections.Immutable.ImmutableArray<Ordering> order = default)
        => (new(partition, sql, AllowFullScan: true), new(partition,
            new(Collection, Alias, selections, predicate, order.IsDefault ? [] : order, Limit), AllowFullScan: true));
}

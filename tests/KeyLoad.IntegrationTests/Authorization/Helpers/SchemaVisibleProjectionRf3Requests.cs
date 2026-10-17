using System.Collections.Immutable;
using KeyLoad.Query;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionRf3Protocol;
using S = KeyLoad.IntegrationTests.Features.Authorization.SafeProjectorOperationRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Requests
{
    internal static IEnumerable<(QueryRequest Sql, AstQueryRequest Ast)> Restricted(SafeProjectorOperationRf3State state)
    {
        var from = P.FromPrefix + state.Collection + P.FromSuffix;
        foreach (var (source, path) in new[]
        {
            (P.PublicSource, S.StatusPath), (P.ProtectedSource, S.LiteralPath), (P.UnknownSource, P.UnknownPath)
        })
        {
            yield return Pair(state, P.SourcePrefix + source + P.AliasSuffix + from,
                [new(path, P.ValueAlias)]);
            yield return Pair(state, P.SourcePrefix + P.Star + from + P.WherePrefix + source + P.WhereSuffix,
                [new(P.Star, P.Star)], new Comparison(new FieldOperand(path), P.Equal, ValueOperand.Create(S.InitialStatus)));
            yield return Pair(state, P.SourcePrefix + P.Star + from + P.OrderPrefix + source,
                [new(P.Star, P.Star)], order: [new(path, false)]);
        }
        var star = Star(state);
        yield return (new(state.Partition, "SELECT d.status AS exposed FROM [" + state.Collection + P.JoinMiddle
            + state.Collection + SchemaVisibleProjectionRf3Joins.TargetSuffix + "] r ON d.status = r.name",
            AllowFullScan: true, QueryDialectVersion: P.Q2), star with
            {
                AstVersion = P.Q2,
                Query = star.Query with
                {
                    Projection = [new(S.StatusPath, P.ValueAlias, P.Alias)],
                    InnerJoin = new(state.Collection + SchemaVisibleProjectionRf3Joins.TargetSuffix, P.RightAlias, S.StatusPath, "/name")
                }
            });
    }

    internal static AstQueryRequest Star(SafeProjectorOperationRf3State state)
        => new(state.Partition, new(state.Collection, P.Alias, [new(P.Star, P.Star)], null, [], P.Limit), AllowFullScan: true);

    private static (QueryRequest Sql, AstQueryRequest Ast) Pair(SafeProjectorOperationRf3State state,
        string sql, ImmutableArray<Selection> projection, Predicate? filter = null, ImmutableArray<Ordering> order = default)
        => (new(state.Partition, sql, AllowFullScan: true), new(state.Partition,
            new(state.Collection, P.Alias, projection, filter, order.IsDefault ? [] : order, P.Limit), AllowFullScan: true));
}

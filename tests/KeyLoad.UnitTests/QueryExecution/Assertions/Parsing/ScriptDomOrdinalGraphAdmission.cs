using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.GraphTraversal;
using P = KeyLoad.UnitTests.Features.QueryExecution.ScriptDomOrdinalParameterProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ScriptDomOrdinalGraphAdmission
{
    internal static async Task RunAsync(TestDatabase database, QueryEngine engine, CancellationToken token)
    {
        foreach (var mismatchedName in new[] { GraphName, DepthName })
        {
            await GraphLabelUseOperations.ObserveAsync(database.Store, async () =>
            {
                var exact = SqlGraphPathTestSupport.Parameters((GraphName, SqlGraphPathTestSupport.Graph),
                    ("fromCollection", SqlGraphPathTestSupport.Orders), ("fromId", SqlGraphPathTestSupport.StartId),
                    ("toCollection", SqlGraphPathTestSupport.Orders), ("toId", SqlGraphPathTestSupport.TargetId),
                    (DepthName, SqlGraphPathTestSupport.PathDepth), ("vertices", SqlGraphPathTestSupport.MaximumVertices),
                    ("edges", SqlGraphPathTestSupport.MaximumEdges), ("label", SqlGraphPathTestSupport.Label));
                var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in exact)
                {
                    values.Add(entry.Key == mismatchedName ? entry.Key.ToUpperInvariant() : entry.Key, entry.Value);
                }
                var before = JsonDefaults.Serialize(values);
                await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
                    SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.ParameterSql, values),
                    ErrorCode.Validation, P.GraphParameter, token);
                var repaired = engine.ShortestPathSql(SqlGraphPathTestSupport.Principal,
                    SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.ParameterSql,
                        new Dictionary<string, JsonElement>(exact, StringComparer.OrdinalIgnoreCase)), cancellationToken: token);
                var literal = engine.ShortestPathSql(SqlGraphPathTestSupport.Principal,
                    SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql), cancellationToken: token);
                await GraphLabelUseOperations.EqualAsync(literal, repaired);
                await Assert.That(JsonDefaults.Serialize(values).AsSpan().SequenceEqual(before)).IsTrue();
            });
        }
    }
    private const string GraphName = "graph";
    private const string DepthName = "depth";
}

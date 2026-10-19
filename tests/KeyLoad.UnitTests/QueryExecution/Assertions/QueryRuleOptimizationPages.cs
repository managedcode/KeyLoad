using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryRuleOptimizationPages
{
    private const int ExpectedScanBudget = 10000;
    private const string FieldUseDenied = "The query requires a protected field-use grant.";
    private const string ScanConsentDenied = "The query requires an index or explicit full-scan permission.";

    internal static Task RunAsync(QueryRuleOptimizationFixture fixture, DatabaseEngine owner, ZoneTreeStore store,
        bool includeNull, bool includeMissing, bool denied, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
            for (var collection = 0; collection < QueryRuleOptimizationData.Collections.Length; collection++)
            {
                foreach (var shape in new[] { QueryRuleOptimizationData.RevisionOneShape, QueryRuleOptimizationData.OpenRevisionOneShape, QueryRuleOptimizationData.OpenRevisionTwoShape, QueryRuleOptimizationData.RawShadowShape, QueryRuleOptimizationData.NestedShape })
                {
                    foreach (var reference in new[] { false, true })
                    {
                        var ast = QueryRuleOptimizationRequests.Ast(fixture.Harness.Partition, collection, shape, reference);
                        var builder = QueryRuleOptimizationRequests.Builder(fixture.Harness.Partition, collection, shape, reference);
                        await RequiredProcessingPolicyAssertions.EqualAsync(ast, builder);
                        var sql = QueryRuleOptimizationRequests.Sql(fixture.Harness.Partition, collection, shape, reference);
                        var path = QueryRuleOptimizationData.Path(collection, shape, reference, includeNull, includeMissing);
                        await QueryAsync(engine, builder, sql, path, shape, store.Position, denied, token);
                    }
                }
            }
        });
    private static async Task QueryAsync(QueryEngine engine, AstQueryRequest ast, QueryRequest sql,
        string path, int shape, long cut, bool denied, CancellationToken token)
    {
        for (var route = 0; route < QueryRuleOptimizationData.RuntimeRouteCount; route++)
        {
            var astPage = ast;
            var sqlPage = sql;
            QueryPage Execute() => route == QueryRuleOptimizationData.AstRoute
                ? engine.ExecuteAst(QueryRuleOptimizationData.Reader, astPage, cancellationToken: token)
                : engine.Execute(QueryRuleOptimizationData.Reader, sqlPage, cancellationToken: token);
            if (denied)
            {
                await FailureAsync(Execute, ErrorCode.PermissionDenied, FieldUseDenied);
                if (route == QueryRuleOptimizationData.AstRoute)
                {
                    var plan = ast with
                    {
                        Query = ast.Query with
                        {
                            Explain = true
                        }
                    };
                    await FailureAsync(() => engine.ExecuteAst(QueryRuleOptimizationData.Reader, plan, cancellationToken: token),
                        ErrorCode.PermissionDenied, FieldUseDenied);
                }
                continue;
            }
            var expected = QueryRuleOptimizationData.Expected(shape);
            for (var offset = 0; offset < Math.Max(1, expected.Length); offset += QueryRuleOptimizationData.PageSize)
            {
                var actual = Execute();
                var rows = expected.Skip(offset).Take(QueryRuleOptimizationData.PageSize).ToArray();
                var final = offset + rows.Length == expected.Length;
                await Assert.That(actual.Cursor is null).IsEqualTo(final);
                await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([.. rows], actual.Cursor, cut, path), actual);
                astPage = astPage with
                {
                    Cursor = actual.Cursor
                };
                sqlPage = sqlPage with
                {
                    Cursor = actual.Cursor
                };
            }
            if (route == QueryRuleOptimizationData.AstRoute)
            {
                await PlanAsync(engine, ast, path, shape, cut, token);
            }
        }
    }
    private static async Task PlanAsync(QueryEngine engine, AstQueryRequest ast, string path, int shape, long cut, CancellationToken token)
    {
        var consent = ast with
        {
            AllowFullScan = false
        };
        var expected = QueryRuleOptimizationData.Expected(shape);
        Func<QueryPage> noConsent = () => engine.ExecuteAst(QueryRuleOptimizationData.Reader, consent, cancellationToken: token);
        if (path == QueryRuleOptimizationData.Scan)
        {
            await FailureAsync(noConsent, ErrorCode.UnsupportedCapability, ScanConsentDenied);
        }
        else
        {
            var actual = noConsent();
            await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([.. expected.Take(QueryRuleOptimizationData.PageSize)],
                actual.Cursor, cut, path), actual);
        }
        var explain = consent with
        {
            AllowFullScan = true,
            Query = consent.Query with
            {
                Explain = true
            }
        };
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            accessPath = path,
            atomicPartition = ast.Partition.AtomicPartitionId,
            scanBudget = ExpectedScanBudget
        }, JsonDefaults.Options);
        await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([new("explain", 0L, body)], null, cut, path),
            engine.ExecuteAst(QueryRuleOptimizationData.Reader, explain, cancellationToken: token));
    }

    private static async Task FailureAsync(Func<QueryPage> execute, ErrorCode code, string detail)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => execute());
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(detail);
    }
}

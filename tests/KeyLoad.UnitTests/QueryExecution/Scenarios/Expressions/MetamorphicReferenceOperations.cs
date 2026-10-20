using System.Collections.Immutable;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class MetamorphicReferenceOperations
{
    internal static string Subject(DatabaseEngine database)
        => database.Authenticate(MetamorphicReferenceData.Secret, database.EvaluationClock.GetUtcNow());
    internal static async Task RunAsync(MetamorphicReferenceFixture fixture, DatabaseEngine database, ZoneTreeStore store,
        MetamorphicReferencePlan[] plans, bool descending, int offset, CancellationToken token)
    {
        var expected = MetamorphicReferenceData.Reference(fixture.Rows, descending, offset);
        await Assert.That(expected.Length).IsGreaterThan(MetamorphicReferenceData.PageSize);
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        for (var shape = 0; shape < plans.Length; shape++)
        {
            var plan = plans[shape];
            var path = MetamorphicReferenceData.Path(shape);
            await PagesAsync(store, expected, path, cursor => engine.Execute(Subject(database),
                plan.Sql with
                {
                    Cursor = cursor
                }, database.EvaluationClock, token));
            foreach (var ast in new[] { plan.Parsed, plan.Json, plan.CSharp })
            {
                await PagesAsync(store, expected, path, cursor => engine.ExecuteAst(Subject(database),
                    ast with
                    {
                        Cursor = cursor
                    }, database.EvaluationClock, token));
                await MetamorphicReferenceLifetime.ObserveAsync(store, async () =>
                {
                    var page = engine.ExecuteAst(Subject(database), ast with
                    {
                        Query = ast.Query with
                        {
                            Explain = true
                        }
                    },
                        database.EvaluationClock, token);
                    var json = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        accessPath = path,
                        atomicPartition = fixture.Harness.Partition.AtomicPartitionId,
                        scanBudget = database.Limits.MaxScanRecords
                    }, JsonDefaults.Options);
                    await MetamorphicReferenceAssertions.PageAsync(page, [new("explain", 0L, json)], store.Position, path, true);
                });
            }
        }
    }
    private static async Task PagesAsync(ZoneTreeStore store, ImmutableArray<MetamorphicReferenceRow> expected,
        string path, Func<string?, QueryPage> execute)
    {
        var cut = store.Position;
        string? cursor = null;
        var seen = 0;
        do
        {
            await MetamorphicReferenceLifetime.ObserveAsync(store, async () =>
            {
                var page = execute(cursor);
                var rows = expected.Skip(seen).Take(MetamorphicReferenceData.PageSize).Select(MetamorphicReferenceData.Expected).ToArray();
                var final = seen + rows.Length == expected.Length;
                await MetamorphicReferenceAssertions.PageAsync(page, rows, cut, path, final);
                seen += rows.Length;
                cursor = page.Cursor;
            });
        } while (cursor is not null);
        await Assert.That(seen).IsEqualTo(expected.Length);
    }
    internal static async Task DeniedAsync(DatabaseEngine database, ZoneTreeStore store, MetamorphicReferencePlan[] plans, CancellationToken token)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        foreach (var plan in plans)
        {
            await DeniedAsync(store, () => engine.Execute(Subject(database), plan.Sql, database.EvaluationClock, token));
            foreach (var ast in new[] { plan.Parsed, plan.Json, plan.CSharp })
            {
                await DeniedAsync(store, () => engine.ExecuteAst(Subject(database), ast, database.EvaluationClock, token));
            }
        }
    }
    private static Task DeniedAsync(ZoneTreeStore store, Func<QueryPage> execute)
        => MetamorphicReferenceLifetime.ObserveAsync(store, async () =>
        {
            QueryPage? partial = null;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = execute());
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(failure.Message).IsEqualTo("The query requires a protected field-use grant.");
            await Assert.That(partial).IsNull();
        });
}

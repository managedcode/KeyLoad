using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;
using P = KeyLoad.UnitTests.Features.QueryExecution.ScriptDomOrdinalParameterProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ScriptDomOrdinalReadAdmission
{
    internal static async Task RunAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database,
        ZoneTreeStore store, CancellationToken token)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        foreach (var name in new[] { P.Count, P.Collection, P.Stream, P.Generation })
        {
            await GraphLabelUseOperations.ObserveAsync(store, async () =>
            {
                var sql = name == P.Count ? P.WindowSql : P.ModelSql;
                var values = Parameters(name == P.Count);
                var mismatched = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in values)
                {
                    mismatched.Add(entry.Key == name ? name.ToUpperInvariant() : entry.Key, entry.Value);
                }
                var original = JsonDefaults.Serialize(mismatched);
                var diagnostics = store.GetReadDiagnostics();
                QueryPage? partial = null;
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.Execute(SqlOffsetProtocol.Reader,
                    new(fixture.Harness.Partition, sql, mismatched, AllowFullScan: true), cancellationToken: token));
                await Assert.That(partial).IsNull();
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
                await Assert.That(failure.Message).IsEqualTo(P.InvalidQuery);
                await Assert.That(store.GetReadDiagnostics()).IsEqualTo(diagnostics);
                await GraphLabelUseOperations.EqualAsync(Expected(name == P.Count, store.Position),
                    engine.Execute(SqlOffsetProtocol.Reader, new(fixture.Harness.Partition, sql, values, AllowFullScan: true),
                        cancellationToken: token));
                await Assert.That(JsonDefaults.Serialize(mismatched).AsSpan().SequenceEqual(original)).IsTrue();
            });
            await SqlParserAdmissionReads.HealthyAsync(fixture, database, store, token);
        }
    }
    private static Dictionary<string, JsonElement> Parameters(bool window)
        => window ? new(StringComparer.OrdinalIgnoreCase)
        {
            [P.Count] = JsonSerializer.SerializeToElement(P.WindowRows)
        } : new(StringComparer.OrdinalIgnoreCase)
        {
            [P.Collection] = JsonSerializer.SerializeToElement(SqlParserAdmissionModels.Events),
            [P.Stream] = JsonSerializer.SerializeToElement("source"),
            [P.Generation] = JsonSerializer.SerializeToElement(P.FirstGeneration)
        };
    private static QueryPage Expected(bool window, long cut)
        => window ? new([new("a", 1L, """{"title":"first"}""", false, []),
            new("b", 1L, """{"title":"second"}""", false, []),
            new("c", 1L, """{"title":"third"}""", false, []),
            new("d", 1L, """{"title":"fourth"}""", false, [])], null, cut, "bounded-full-scan")
        : new([new("created", 1L, """{"value":1}""", false, []),
            new("updated", 2L, """{"value":2}""", false, [])], null, cut, "model-scan:events");
}

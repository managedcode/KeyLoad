using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class ModelSourceArgumentRefusals
{
    internal static async Task RequireAsync(SqlModelParserOperationFixture fixture)
    {
        var engine = new QueryEngine(fixture.Owner, UnitExecutionOptions.QueryExecution());
        foreach (var (refused, healthy, events) in ModelSourceArgumentRequests.All(fixture.Partition))
        {
            await ObserveAsync(fixture, async () =>
            {
                var diagnostics = fixture.Store.GetReadDiagnostics();
                var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlModelParserOperationData.Reader, refused));
                await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
                await Assert.That(error.Message).IsEqualTo(Unsupported);
                await Assert.That(fixture.Store.GetReadDiagnostics()).IsEqualTo(diagnostics);
            });
            await ObserveAsync(fixture, async () =>
            {
                var actual = engine.Execute(SqlModelParserOperationData.Reader, healthy);
                await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(
                    JsonDefaults.Serialize(Expected(fixture, events)))).IsTrue();
            });
            await ObserveAsync(fixture, () => SqlModelParserPages.RequireAsync(fixture));
        }
    }

    private static QueryPage Expected(SqlModelParserOperationFixture fixture, bool events)
        => events
            ? new([new("event-b", 2L, """{"eventType":"Updated"}""", false, []),
                new("event-a", 1L, """{"eventType":"Created"}""", false, [])], null,
                fixture.Store.Position, "model-scan:events")
            : new([new("message-b", 1L, """{"id":"message-b","state":"Ready"}""", false, []),
                new("message-a", 1L, """{"id":"message-a","state":"Ready"}""", false, [])], null,
                fixture.Store.Position, "model-scan:queue-messages");

    private static async Task ObserveAsync(SqlModelParserOperationFixture fixture, Func<Task> operation)
    {
        var cut = SqlModelParserConservation.Capture(fixture);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(fixture, cut), failures);
        await ServerFailureObserver.ObserveAsync(() => SqlModelParserConservation.ReplayAsync(fixture), failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(fixture, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ConserveAsync(SqlModelParserOperationFixture fixture, SqlModelParserStoreCut before)
    {
        var after = SqlModelParserConservation.Capture(fixture);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(after.Position).IsEqualTo(before.Position), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(after.Image).IsEqualTo(before.Image), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(after.Identity.SequenceEqual(before.Identity)).IsTrue(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private const string Unsupported = "The SQL statement or execution clause is unsupported.";
}

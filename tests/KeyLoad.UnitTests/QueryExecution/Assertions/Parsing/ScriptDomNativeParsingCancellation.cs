namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ScriptDomNativeParsingCancellation
{
    internal static async Task RequireAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
        => await RequireAsync(database, engine, ScriptDomSelectPages.WindowSql);

    internal static async Task RequireAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine, string sql)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => ObservedAsync(database, engine, cancellation, sql, failures), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
    private static async Task ObservedAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        CancellationTokenSource cancellation, string sql, List<Exception> failures)
    {
        var original = cancellation.Token;
        var state = new ScriptDomCancellationState(cancellation);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var observation = global::KeyLoad.Features.QueryExecution.ScriptDomParsingObservation.Enter(state.OnToken);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var operation = Task.Run(() => engine.Execute(SqlOffsetProtocol.Reader,
                    new(database.Partition, sql, AllowFullScan: true), cancellationToken: original));
                await JoinedAsync(operation, original);
                await Assert.That(state.Observed).IsTrue();
            }, failures);
        }, failures);
    }
    private static async Task JoinedAsync(Task<QueryPage> original, CancellationToken token)
    {
        try
        {
            _ = await original;
        }
        catch (OperationCanceledException failure)
        {
            await Assert.That(failure.CancellationToken).IsEqualTo(token);
            return;
        }
        throw new InvalidOperationException(RequiredCancellation);
    }
    private const string RequiredCancellation = "The original admitted native parser operation did not retain cancellation.";
}

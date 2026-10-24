namespace KeyLoad.UnitTests.QueryExecution;

internal static class ScriptDomSelectCancellation
{
    internal static async Task RequireAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var cancellationImage = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var cancellationPosition = database.Store.Position;
        var cancellationIdentity = JsonDefaults.Serialize(database.Store.Identity);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var original = cancellation.Token;
                var failure = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlOffsetProtocol.Reader,
                    new(database.Partition, ScriptDomSelectPages.WindowSql, AllowFullScan: true), cancellationToken: original));
                await Assert.That(failure.CancellationToken).IsEqualTo(original);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, cancellationImage, cancellationPosition, cancellationIdentity);
        await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, cancellationImage, cancellationPosition, cancellationIdentity);
    }
}
